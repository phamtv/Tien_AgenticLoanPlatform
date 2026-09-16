using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LoanPlatform.Orchestrator;

/// <summary>
/// The agentic replacement for the auto-processing events we removed from
/// Origination/Underwriting/Funding. Where the old event handlers advanced
/// an application automatically the instant the previous step finished,
/// this class drives each stage explicitly: it reads the application's real
/// status from Origination (the source of truth — this class holds no state
/// of its own between runs), hands Claude only the tools relevant to the
/// current stage, and only calls fund_loan/disburse_loan itself — never
/// lets Claude call it directly — once a human has confirmed it at the
/// console. Underwriting decisions (evaluate_application) are left to
/// Claude's own tool call, since that's a data-recording judgment call, not
/// a movement of real money.
///
/// UNVERIFIED — see OrchestratorService.csproj's header comment. This is
/// the first version; expect to fix a handful of Microsoft.Extensions.AI /
/// ModelContextProtocol member names once you actually build it.
/// </summary>
public class LoanApplicationOrchestrator
{
    private readonly IChatClient _chat;
    private readonly McpClient _mcp;
    private readonly IList<McpClientTool> _allTools;
    private readonly string _model;

    public LoanApplicationOrchestrator(IChatClient chat, McpClient mcp, IList<McpClientTool> allTools, string model)
    {
        _chat = chat;
        _mcp = mcp;
        _allTools = allTools;
        _model = model;
    }

    public async Task RunAsync(string applicationId, CancellationToken ct = default)
    {
        Console.WriteLine($"=== Orchestrating application {applicationId} ===");

        var status = await GetApplicationStatusAsync(applicationId, ct);
        Console.WriteLine($"Current status: {status}");

        if (status is "Submitted" or "UnderwritingInProgress")
        {
            var escalated = await RunUnderwritingStageAsync(applicationId, ct);
            if (escalated)
            {
                Console.WriteLine("Underwriting stage escalated for human review. Stopping — no automatic action taken.");
                return;
            }

            status = await GetApplicationStatusAsync(applicationId, ct);
            Console.WriteLine($"Status after underwriting: {status}");
        }

        if (status == "Denied")
        {
            Console.WriteLine($"Application {applicationId} was denied. Nothing further for the orchestrator to do.");
            return;
        }

        if (status != "Approved")
        {
            Console.WriteLine($"Unexpected status '{status}' after underwriting — stopping for human review rather than guessing what to do next.");
            return;
        }

        var funded = await RunFundingStageAsync(applicationId, ct);
        if (!funded)
        {
            Console.WriteLine("Funding was not completed this run (escalated, or the reviewer declined). Stopping.");
            return;
        }

        await ConfirmServicingAsync(applicationId, ct);
        Console.WriteLine($"=== Done: application {applicationId} is funded and active in servicing ===");
    }

    // --- Stage 1: Underwriting ---
    // Claude may call evaluate_application itself here — recording a
    // decision is a judgment call worth an agent making, not a movement of
    // money, so it doesn't need the human gate the funding stage below has.
    private async Task<bool> RunUnderwritingStageAsync(string applicationId, CancellationToken ct)
    {
        var tools = FilterTools("get_application", "get_application_documents", "run_credit_check", "get_risk_score", "evaluate_application", "get_decision");

        const string systemPrompt = """
            You are the underwriting stage of an auto loan orchestrator. You will be given one
            application ID. Your job:

            1. Call get_application to read its stored applicant, employment, and vehicle data.
            2. Call get_application_documents to see what's on file. If required financial
               documents are obviously missing or the stored data looks inconsistent (e.g. income
               and vehicle value don't match what a normal applicant in this situation would show),
               do NOT proceed — respond with a single line starting with "ESCALATE:" explaining why,
               and take no further action.
            3. Otherwise, call run_credit_check to get a fresh credit score and debt figures.
            4. Call evaluate_application with the REAL figures you just gathered — never invent or
               estimate a credit score, income, debt, or vehicle value. Use exactly what the tools
               returned.
            5. After evaluate_application returns, briefly summarize the decision in plain English
               as your final answer (approved/denied, amount, rate, reason).

            If anything looks borderline enough that a human underwriter should look at it before
            evaluate_application is called, respond with "ESCALATE: <reason>" instead of calling it.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, $"Process application {applicationId} through underwriting now."),
        };

        var response = await _chat.GetResponseAsync(messages, new ChatOptions { Tools = [.. tools] }, ct);
        Console.WriteLine($"[Underwriting] {response.Text}");

        return response.Text.Contains("ESCALATE", StringComparison.OrdinalIgnoreCase);
    }

    // --- Stage 2: Funding ---
    // Deliberately does NOT give Claude fund_loan/disburse_loan. Claude only
    // verifies readiness and recommends; this method is the only thing that
    // ever calls fund_loan, and only after a human types "yes" at the
    // console, with the disbursement amount/rate/term read directly from
    // Underwriting's decision record rather than re-derived by the model.
    private async Task<bool> RunFundingStageAsync(string applicationId, CancellationToken ct)
    {
        var decisionBody = await CallToolForBodyAsync("get_decision", new Dictionary<string, object?> { ["applicationId"] = applicationId }, ct);
        if (decisionBody is null)
        {
            Console.WriteLine($"Could not read a decision record for {applicationId} — stopping.");
            return false;
        }

        var approvedAmount = GetDecimal(decisionBody.Value, "approvedAmount");
        var interestRate = GetDecimal(decisionBody.Value, "interestRate");
        var termMonths = GetInt(decisionBody.Value, "termMonths") ?? 60;

        if (approvedAmount is null || interestRate is null)
        {
            Console.WriteLine($"Decision record for {applicationId} is missing approvedAmount/interestRate — stopping for human review.");
            return false;
        }

        var verifyTools = FilterTools("get_decision", "verify_before_funding", "get_funding_status");

        const string systemPrompt = """
            You are the funding-readiness stage of an auto loan orchestrator. You cannot disburse
            funds yourself — you only verify readiness and report a recommendation. Steps:

            1. Call get_funding_status to make sure this application hasn't already been funded.
               If it has, say so plainly and stop.
            2. Call verify_before_funding to run the pre-disbursement identity/title re-check.
            3. If everything checks out, your ENTIRE final answer must be exactly one line, with
               no other sentences before or after it, starting with exactly "READY TO FUND:"
               followed by a one-sentence summary. Do not narrate what you're about to do first —
               go straight from the tool calls to that one line.
            4. If anything looks wrong (already funded, verification failed, anything
               inconsistent), respond with "ESCALATE:" followed by the reason, and do not
               recommend funding.
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, $"Check whether application {applicationId} is ready to fund."),
        };

        var response = await _chat.GetResponseAsync(messages, new ChatOptions { Tools = [.. verifyTools] }, ct);
        Console.WriteLine($"[Funding readiness] {response.Text}");

        // Contains rather than a strict StartsWith: the model doesn't always
        // put the marker as the literal first character (e.g. it may add a
        // short preamble sentence first despite the prompt asking it not
        // to) — treating this as a rigid parse target is more fragile than
        // the signal is worth. What matters is that the marker appears at
        // all and ESCALATE doesn't.
        var readyToFund = response.Text.Contains("READY TO FUND:", StringComparison.OrdinalIgnoreCase);
        var escalated = response.Text.Contains("ESCALATE", StringComparison.OrdinalIgnoreCase);

        if (!readyToFund || escalated)
        {
            Console.WriteLine("Funding stage did not confirm readiness — stopping without disbursing.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"Recommended action: disburse ${approvedAmount:N2} at {interestRate}% over {termMonths} months for {applicationId}.");
        Console.Write("Proceed with disbursement? Type 'yes' to confirm, anything else to cancel: ");
        var confirmation = Console.ReadLine();

        if (!string.Equals(confirmation?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Disbursement cancelled by reviewer.");
            return false;
        }

        // Deterministic execution — the orchestrator's own code calls
        // fund_loan directly with the values read from the decision record
        // above, rather than trusting the model to reconstruct them.
        var fundResult = await _mcp.CallToolAsync("fund_loan", new Dictionary<string, object?>
        {
            ["applicationId"] = applicationId,
            ["approvedAmount"] = approvedAmount.Value,
            ["interestRate"] = interestRate.Value,
            ["termMonths"] = termMonths,
        }, cancellationToken: ct);

        Console.WriteLine($"[Funding] {GetText(fundResult)}");
        return true;
    }

    // --- Stage 3: confirm Servicing picked it up ---
    // LoanFundedEventHandler in Servicing (left wired — see FundingService/
    // Program.cs's AGENTIC MIGRATION comment) reacts to fund_loan's publish
    // and creates the loan record asynchronously, so this may need a brief
    // moment before the loan appears.
    private async Task ConfirmServicingAsync(string applicationId, CancellationToken ct)
    {
        var fundingBody = await CallToolForBodyAsync("get_funding", new Dictionary<string, object?> { ["applicationId"] = applicationId }, ct);
        var loanId = fundingBody is { } body ? GetString(body, "loanId") : null;

        if (loanId is null)
        {
            Console.WriteLine("Could not read the funding record back to find the LoanId — check Servicing manually.");
            return;
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var loanBody = await CallToolForBodyAsync("get_loan", new Dictionary<string, object?> { ["loanId"] = loanId }, ct);
            if (loanBody is not null)
            {
                Console.WriteLine($"Servicing confirms loan {loanId} is active (attempt {attempt}).");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        Console.WriteLine($"Loan {loanId} was funded but hasn't appeared in Servicing yet after a few checks — worth confirming manually.");
    }

    // --- Helpers ---

    private List<McpClientTool> FilterTools(params string[] names) =>
        _allTools.Where(t => names.Contains(t.Name)).ToList();

    private async Task<string> GetApplicationStatusAsync(string applicationId, CancellationToken ct)
    {
        var body = await CallToolForBodyAsync("get_application_status", new Dictionary<string, object?> { ["applicationId"] = applicationId }, ct);
        return body is { } b ? GetString(b, "status") ?? "Unknown" : "Unknown";
    }

    /// <summary>
    /// Calls an MCP tool directly (bypassing Claude) and unwraps the
    /// ApiResult envelope every tool in this platform returns — see
    /// Services/McpServer/Http/LoanPlatformApiClient.cs's ApiResult record
    /// (StatusCode, Success, Body, Error). Returns the Body element, or null
    /// if the call failed or returned no body (e.g. a 404).
    /// </summary>
    private async Task<JsonElement?> CallToolForBodyAsync(string toolName, Dictionary<string, object?> args, CancellationToken ct)
    {
        var result = await _mcp.CallToolAsync(toolName, args, cancellationToken: ct);
        var text = GetText(result);
        if (string.IsNullOrWhiteSpace(text)) return null;

        try
        {
            var root = JsonDocument.Parse(text).RootElement;
            if (root.TryGetProperty("body", out var body) && body.ValueKind != JsonValueKind.Null)
                return body;
            if (root.TryGetProperty("Body", out var bodyPascal) && bodyPascal.ValueKind != JsonValueKind.Null)
                return bodyPascal;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

    private static string? GetString(JsonElement element, string propertyName) =>
        TryGetPropertyCaseInsensitive(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static decimal? GetDecimal(JsonElement element, string propertyName) =>
        TryGetPropertyCaseInsensitive(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;

    private static int? GetInt(JsonElement element, string propertyName) =>
        TryGetPropertyCaseInsensitive(element, propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
