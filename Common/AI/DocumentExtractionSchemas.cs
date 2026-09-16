using System.Text.Json.Nodes;

namespace LoanPlatform.Common.AI;

/// <summary>
/// Claude tool-use ("input_schema") definitions for the four document
/// types this platform accepts as loan-package attachments. Forcing
/// tool_choice against one of these guarantees Claude's response is
/// exactly this shape — same principle as OpenAI's Structured Outputs
/// (json_schema + strict: true), just expressed as Claude's tool-use
/// mechanism instead.
///
/// Field sets deliberately mirror what a human underwriter would key in
/// by hand from these documents — see ApplicationsController's
/// SubmitApplicationRequest (employer name, monthly income, etc.) for
/// what these are meant to help verify against.
/// </summary>
public static class DocumentExtractionSchemas
{
    public static readonly string[] SupportedTypes = ["pay_stub", "w2", "bank_statement", "id_document"];

    public static JsonObject Build(string documentType) => documentType switch
    {
        "pay_stub" => PayStub(),
        "w2" => W2(),
        "bank_statement" => BankStatement(),
        "id_document" => IdDocument(),
        _ => throw new ArgumentException(
            $"Unknown documentType '{documentType}'. Expected one of: {string.Join(", ", SupportedTypes)}."),
    };

    private static JsonObject StringProp(string? description = null)
    {
        var obj = new JsonObject { ["type"] = new JsonArray("string", "null") };
        if (description is not null) obj["description"] = description;
        return obj;
    }

    private static JsonObject NumberProp(string? description = null)
    {
        var obj = new JsonObject { ["type"] = new JsonArray("number", "null") };
        if (description is not null) obj["description"] = description;
        return obj;
    }

    private static JsonObject ConfidenceFlags() => new()
    {
        ["type"] = "array",
        ["items"] = new JsonObject { ["type"] = "string" },
        ["description"] = "Names of fields you could not read confidently, or that were absent from the document.",
    };

    private static JsonObject Tool(string name, string description, JsonObject properties, string[] required) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["input_schema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
        },
    };

    private static JsonObject PayStub()
    {
        var props = new JsonObject
        {
            ["employeeName"] = StringProp(),
            ["employerName"] = StringProp(),
            ["payPeriodStart"] = StringProp("YYYY-MM-DD"),
            ["payPeriodEnd"] = StringProp("YYYY-MM-DD"),
            ["payDate"] = StringProp("YYYY-MM-DD"),
            ["grossPayCurrent"] = NumberProp(),
            ["grossPayYtd"] = NumberProp(),
            ["netPayCurrent"] = NumberProp(),
            ["payFrequency"] = StringProp("One of: Weekly, Biweekly, Semimonthly, Monthly"),
            ["confidenceFlags"] = ConfidenceFlags(),
        };
        return Tool("extract_pay_stub", "Extract structured fields from a pay stub image or PDF.", props,
        [
            "employeeName", "employerName", "payPeriodStart", "payPeriodEnd", "payDate",
            "grossPayCurrent", "grossPayYtd", "netPayCurrent", "payFrequency", "confidenceFlags",
        ]);
    }

    private static JsonObject W2()
    {
        var props = new JsonObject
        {
            ["taxYear"] = NumberProp(),
            ["employeeName"] = StringProp(),
            ["employerName"] = StringProp(),
            ["wagesBox1"] = NumberProp("Box 1 — wages, tips, other compensation"),
            ["federalTaxWithheldBox2"] = NumberProp("Box 2"),
            ["socialSecurityWagesBox3"] = NumberProp("Box 3"),
            ["medicareWagesBox5"] = NumberProp("Box 5"),
            ["confidenceFlags"] = ConfidenceFlags(),
        };
        return Tool("extract_w2", "Extract structured fields from an IRS W-2 form image or PDF.", props,
        [
            "taxYear", "employeeName", "employerName", "wagesBox1",
            "federalTaxWithheldBox2", "socialSecurityWagesBox3", "medicareWagesBox5", "confidenceFlags",
        ]);
    }

    private static JsonObject BankStatement()
    {
        var props = new JsonObject
        {
            ["accountHolderName"] = StringProp(),
            ["bankName"] = StringProp(),
            ["statementStartDate"] = StringProp("YYYY-MM-DD"),
            ["statementEndDate"] = StringProp("YYYY-MM-DD"),
            ["beginningBalance"] = NumberProp(),
            ["endingBalance"] = NumberProp(),
            ["totalDeposits"] = NumberProp(),
            ["totalWithdrawals"] = NumberProp(),
            ["nsfOrOverdraftCount"] = NumberProp("Count of NSF/overdraft fees in the statement period"),
            ["confidenceFlags"] = ConfidenceFlags(),
        };
        return Tool("extract_bank_statement", "Extract structured summary fields from a bank statement image or PDF.", props,
        [
            "accountHolderName", "bankName", "statementStartDate", "statementEndDate", "beginningBalance",
            "endingBalance", "totalDeposits", "totalWithdrawals", "nsfOrOverdraftCount", "confidenceFlags",
        ]);
    }

    private static JsonObject IdDocument()
    {
        var props = new JsonObject
        {
            ["fullName"] = StringProp(),
            ["dateOfBirth"] = StringProp("YYYY-MM-DD"),
            ["documentNumber"] = StringProp(),
            ["issuingStateOrCountry"] = StringProp(),
            ["expirationDate"] = StringProp("YYYY-MM-DD"),
            ["address"] = StringProp(),
            ["confidenceFlags"] = ConfidenceFlags(),
        };
        return Tool("extract_id_document",
            "Extract structured fields from a government-issued ID (driver's license, state ID, or passport) image or PDF.",
            props,
            ["fullName", "dateOfBirth", "documentNumber", "issuingStateOrCountry", "expirationDate", "address", "confidenceFlags"]);
    }
}
