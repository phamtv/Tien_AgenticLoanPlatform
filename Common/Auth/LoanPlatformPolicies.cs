using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace LoanPlatform.Common.Auth;

/// <summary>
/// Named authorization policies shared across all four services — one
/// place that maps a business capability (e.g. "can approve/deny a
/// loan") to the set of App Roles allowed to do it, instead of scattering
/// [Authorize(Roles = "Underwriter,Admin")] role-name strings across
/// individual controllers. Call AddLoanPlatformAuthorizationPolicies()
/// right after AddLoanPlatformEntraIdAuth() in each service's Program.cs
/// — that order matters (this only adds policies; the other call is what
/// actually registers the authentication/authorization services these
/// policies attach to).
///
/// Admin is included in every policy on purpose — a full administrator
/// can act as any role.
///
/// Orchestrator was originally scoped to only the specific actions
/// LoanApplicationOrchestrator.cs's FilterTools() restricts itself to per
/// pipeline stage: run a credit check, evaluate an application, verify
/// before funding, and fund. That restriction used to be enforced only at
/// the prompt/tool-list level inside the orchestrator's own C# code —
/// soft, and only as good as the LLM actually respecting it. These
/// policies made it a hard floor at the API layer too: even if something
/// got the model to call a tool outside its intended stage, the
/// underlying service would reject it unless the caller's token actually
/// carried a role allowed to do that.
///
/// *** TODO(tien): REVERT THIS — TEMP-FULL-ACCESS grant added 2026-09-16 ***
/// Every policy below now also grants Orchestrator, making it temporarily
/// equivalent to Admin. This was done to unblock testing (submitting
/// applications, running Claude directly against the MCP server, etc.)
/// after discovering Entra ID won't let a service principal hold Admin
/// (Admin's app role has allowedMemberTypes: User only — see
/// LoanPlatformRoles.cs). It intentionally removes the "submitting/
/// editing applications, uploading documents, and the underwriter
/// co-pilot summary stay human-only" boundary described above, plus
/// Servicing's "no Orchestrator anywhere — servicing is entirely
/// human-owned" boundary further down. Because the MCP server's
/// Orchestrator identity is a single fixed token shared by every caller
/// (the standalone orchestrator pipeline AND Claude via MCP), this grant
/// applies everywhere that token is used, not just to ad-hoc chat
/// testing — treat it as platform-wide until reverted.
/// TO REVERT: search this file for "TEMP-FULL-ACCESS", delete every
/// `LoanPlatformRoles.Orchestrator` argument tagged with that comment,
/// delete this TODO paragraph, and rebuild/redeploy all four services
/// (origination, underwriting, funding, servicing).
/// </summary>
public static class LoanPlatformPolicies
{
    // --- Origination ---
    public const string OriginationSubmitApplications = "Origination.SubmitApplications";
    public const string OriginationViewApplications = "Origination.ViewApplications";
    public const string OriginationEditApplications = "Origination.EditApplications";
    public const string OriginationManageDocuments = "Origination.ManageDocuments";
    public const string OriginationViewDocuments = "Origination.ViewDocuments";
    public const string OriginationRunCreditCheck = "Origination.RunCreditCheck";

    // --- Underwriting ---
    public const string UnderwritingViewDecisions = "Underwriting.ViewDecisions";
    public const string UnderwritingEvaluate = "Underwriting.Evaluate";
    public const string UnderwritingCopilotSummary = "Underwriting.CopilotSummary";

    // --- Funding ---
    public const string FundingViewFunding = "Funding.ViewFunding";
    public const string FundingViewFundingStatus = "Funding.ViewFundingStatus";
    public const string FundingVerify = "Funding.Verify";
    public const string FundingDisburse = "Funding.Disburse";

    // --- Servicing ---
    public const string ServicingViewLoans = "Servicing.ViewLoans";
    public const string ServicingViewLoanStatus = "Servicing.ViewLoanStatus";
    public const string ServicingPostPayments = "Servicing.PostPayments";
    public const string ServicingUpdateStatus = "Servicing.UpdateStatus";
    public const string ServicingGenerateStatements = "Servicing.GenerateStatements";

    public static IServiceCollection AddLoanPlatformAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()

            // --- Origination ---
            // New application intake and edits stay strictly human —
            // Loan Officer owns the relationship with the borrower.
            .AddPolicy(OriginationSubmitApplications, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            .AddPolicy(OriginationEditApplications, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            // Viewing an application/status is needed by almost everyone
            // downstream, including the orchestrator (get_application).
            .AddPolicy(OriginationViewApplications, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.Underwriter, LoanPlatformRoles.LoanProcessor,
                LoanPlatformRoles.RiskComplianceOfficer, LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            // Uploading/extracting documents is Loan Officer/Loan
            // Processor territory — collecting and verifying paperwork is
            // exactly what a processor sits in the pipeline to do.
            .AddPolicy(OriginationManageDocuments, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.LoanProcessor, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            // Reading documents (not uploading) is needed more broadly —
            // an underwriter reviewing stipulations, compliance auditing,
            // and the orchestrator's get_application_documents tool.
            .AddPolicy(OriginationViewDocuments, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.LoanProcessor, LoanPlatformRoles.Underwriter,
                LoanPlatformRoles.RiskComplianceOfficer, LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            .AddPolicy(OriginationRunCreditCheck, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.LoanProcessor,
                LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))

            // --- Underwriting ---
            .AddPolicy(UnderwritingViewDecisions, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.Underwriter, LoanPlatformRoles.LoanProcessor,
                LoanPlatformRoles.FundingSpecialist, LoanPlatformRoles.RiskComplianceOfficer,
                LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            // The actual approve/deny judgment call — Underwriter and the
            // orchestrator only (evaluate_application).
            .AddPolicy(UnderwritingEvaluate, p => p.RequireRole(
                LoanPlatformRoles.Underwriter, LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            // Originally deliberately human-only — see UnderwritingController's
            // own doc comment on GetCopilotSummary: this is only ever meant
            // to run when a human clicks "Generate summary," never
            // automatically. Orchestrator added below as part of the
            // TEMP-FULL-ACCESS grant (see class doc comment) — revert this too.
            .AddPolicy(UnderwritingCopilotSummary, p => p.RequireRole(
                LoanPlatformRoles.Underwriter, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))

            // --- Funding ---
            .AddPolicy(FundingViewFunding, p => p.RequireRole(
                LoanPlatformRoles.FundingSpecialist, LoanPlatformRoles.RiskComplianceOfficer,
                LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            // Narrower than FundingViewFunding on purpose: this only backs
            // FundingsController.GetStatus, the lightweight "Funded" /
            // "NotFunded" check with no financial detail (no amount, rate,
            // disbursement method). Loan Officer and Loan Processor don't
            // own Funding's records, but they do own the customer
            // relationship and legitimately need to know where a loan
            // stands — "it's funded" is a status fact, not a financial
            // disclosure. Everyone who can see the full record can see the
            // status too, so this is a superset of FundingViewFunding's
            // role list, not a separate track.
            .AddPolicy(FundingViewFundingStatus, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.LoanProcessor,
                LoanPlatformRoles.FundingSpecialist, LoanPlatformRoles.RiskComplianceOfficer,
                LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            .AddPolicy(FundingVerify, p => p.RequireRole(
                LoanPlatformRoles.FundingSpecialist, LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))
            // Moves real money — the compliance-sensitive step. The
            // human-in-the-loop confirmation before this is still enforced
            // in LoanApplicationOrchestrator.cs's own code (the funding
            // stage's Console.ReadLine gate); this policy is the floor
            // underneath that, not a replacement for it.
            .AddPolicy(FundingDisburse, p => p.RequireRole(
                LoanPlatformRoles.FundingSpecialist, LoanPlatformRoles.Orchestrator, LoanPlatformRoles.Admin))

            // --- Servicing ---
            // Originally: no Orchestrator anywhere in Servicing — the
            // automated pipeline's job ends at funding; loan servicing
            // afterward is entirely human-owned. Orchestrator added to
            // every policy below as part of the TEMP-FULL-ACCESS grant
            // (see class doc comment) — revert this whole section too.
            .AddPolicy(ServicingViewLoans, p => p.RequireRole(
                LoanPlatformRoles.Servicer, LoanPlatformRoles.CollectionsAgent,
                LoanPlatformRoles.RiskComplianceOfficer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            // Same narrowing as FundingViewFundingStatus above, applied to
            // Servicing: backs LoansController's applicationId-keyed status
            // lookup only (Active/Delinquent/PaidOff/NotStarted, no
            // balance, payment history, or schedule). Loan Officer and Loan
            // Processor get this even though they have no other Servicing
            // access.
            .AddPolicy(ServicingViewLoanStatus, p => p.RequireRole(
                LoanPlatformRoles.LoanOfficer, LoanPlatformRoles.LoanProcessor,
                LoanPlatformRoles.Servicer, LoanPlatformRoles.CollectionsAgent,
                LoanPlatformRoles.RiskComplianceOfficer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            .AddPolicy(ServicingPostPayments, p => p.RequireRole(
                LoanPlatformRoles.Servicer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            // Collections Agent needs this to flag a loan Delinquent.
            // Note this is an endpoint-level policy only — nothing here
            // stops a Collections Agent from technically setting any of
            // the three statuses (Active/Delinquent/PaidOff), not just
            // Delinquent. Restricting that is a value-level business rule
            // that would need an explicit check inside UpdateLoan itself,
            // not something role-based authorization alone can express.
            .AddPolicy(ServicingUpdateStatus, p => p.RequireRole(
                LoanPlatformRoles.Servicer, LoanPlatformRoles.CollectionsAgent, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */))
            .AddPolicy(ServicingGenerateStatements, p => p.RequireRole(
                LoanPlatformRoles.Servicer, LoanPlatformRoles.Admin, LoanPlatformRoles.Orchestrator /* TEMP-FULL-ACCESS */));

        return services;
    }
}
