using System.Text.Json.Nodes;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Claude tool-use ("input_schema") definition for the underwriter
/// co-pilot summary — same forced tool_choice principle as
/// DocumentExtractionSchemas, just a single tool instead of a per-document
/// switch, since this feature only ever produces one shape of output.
///
/// This is deliberately NOT a decision. There is no "recommendation" or
/// "approve"/"deny" field anywhere in this schema — see
/// ClaudeUnderwritingCopilotService's system prompt for why that's a hard
/// constraint, not an oversight: this tool exists to help a human
/// underwriter read an application faster, not to make the call for them.
/// </summary>
public static class UnderwritingCopilotSchemas
{
    public const string ToolName = "summarize_application_for_underwriter";

    public static JsonObject Build()
    {
        var props = new JsonObject
        {
            ["summary"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "2-4 sentence neutral overview of the application and its current decision — who the applicant is, what they're financing, and the risk figures already on record. Descriptive only, never a recommendation.",
            },
            ["riskFactors"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Specific factors visible in the provided data that a human underwriter would want to weigh — e.g. thin employment history, self-employed/variable income, DTI near the policy threshold. Empty array if genuinely nothing stands out.",
            },
            ["inconsistencies"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Concrete mismatches between fields in the provided data (e.g. a stated figure that doesn't line up with another field in the same record). Empty array if none found — never invent one to fill this list.",
            },
            ["suggestedStipulations"] = new JsonObject
            {
                ["type"] = "array",
                ["items"] = new JsonObject { ["type"] = "string" },
                ["description"] = "Additional documentation or conditions the underwriter might reasonably ask for before proceeding, beyond whatever stipulations are already on record. Empty array if nothing further seems warranted.",
            },
        };

        return new JsonObject
        {
            ["name"] = ToolName,
            ["description"] = "Summarize a loan application and its underwriting decision for a human underwriter's review. Advisory only — never state or imply whether the application should be approved or denied.",
            ["input_schema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray("summary", "riskFactors", "inconsistencies", "suggestedStipulations"),
            },
        };
    }
}
