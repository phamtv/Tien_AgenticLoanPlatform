namespace LoanPlatform.Common.Auth;

/// <summary>
/// Entra ID App Role values — see the LoanPlatform-MCP app registration's
/// Manifest -> appRoles. Microsoft.Identity.Web (wired up in
/// EntraIdAuthExtensions.cs's AddLoanPlatformEntraIdAuth) sets
/// TokenValidationParameters.RoleClaimType to Azure AD's "roles" claim
/// automatically, so [Authorize(Roles = "...")] / RequireRole() work
/// against these values with no extra mapping code — that's the whole
/// reason this is just a list of strings and not a custom claims
/// transformer.
///
/// These values must match each app role's "value" field in the Entra
/// admin center character-for-character (including case) — that's
/// literally what ends up in the token.
///
/// Two roles are humans-only by construction (allowedMemberTypes: User
/// on the app registration) — Loan Officer through Admin below —
/// and one is application-only (allowedMemberTypes: Application) —
/// Orchestrator, assigned to the MCP server's own service principal, not
/// to a person. See LoanPlatformPolicies.cs for how these combine into
/// actual per-endpoint authorization.
///
/// LoanPlatform.Access (the app's original, coarse "can call the
/// platform APIs at all" role) is deliberately NOT listed here. It
/// predates this per-endpoint role model, nothing in LoanPlatformPolicies
/// checks for it, and it's left alone on the app registration rather than
/// removed (an app role with an existing assignment can't be deleted
/// without disabling it first) — it's inert now, not wired into anything.
/// </summary>
public static class LoanPlatformRoles
{
    // --- Human roles (Entra ID users, signed in via the UI's MSAL flow) ---
    public const string LoanOfficer = "LoanOfficer";
    public const string Underwriter = "Underwriter";
    public const string LoanProcessor = "LoanProcessor";
    public const string FundingSpecialist = "FundingSpecialist";
    public const string Servicer = "Servicer";
    public const string RiskComplianceOfficer = "RiskComplianceOfficer";
    public const string CollectionsAgent = "CollectionsAgent";
    public const string Admin = "Admin";

    // --- Machine role (the MCP server's own app-only client-credentials identity) ---
    public const string Orchestrator = "Orchestrator";
}
