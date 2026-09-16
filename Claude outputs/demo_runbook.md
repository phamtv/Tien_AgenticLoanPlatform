# Agentic Loan Platform — Demo Runbook (Jordan Rivera / subprime scenario)

Reproducible steps for the demo scenario already run against
`Tien_AgenticLoanPlatform`. Each fresh `submit_application` call generates a
**new** `ApplicationId`, so you can't literally reuse `APP-DECC702C` — this
runbook lets you recreate the same applicant/vehicle/credit profile on demand.

## 1. Prerequisites

- Stack is up: `docker compose up --build -d` from the repo root, all 9
  containers healthy (`docker compose ps`).
- `loan-platform` local MCP connector is connected (not `Failed` in Claude
  desktop's Local MCP servers panel).
- `Services/OrchestratorService/appsettings.json` has real values for
  `Mcp:ApiKey` (must match `MCP_API_KEY` in `.env`) and `Anthropic:ApiKey`.

## 2. Submit the application

Call the MCP tool `submit_application` (via Claude, or any MCP client hitting
the `loan-platform` connector) with the payload in
`jordan_rivera_application_payload.json` (same folder as this file).

This applicant profile is deliberately **subprime**: the platform's simulated
credit check consistently returns a low score (~595, Experian FICO Auto Score
8) for this SSN/name/DOB combination, which is what makes it a good demo of
Claude's judgment calls in underwriting rather than an automatic rubber-stamp
approval.

Expected immediate response:
- `status: "UnderwritingInProgress"` (submission runs credit + identity
  checks automatically, but does **not** auto-advance to a decision — that's
  the whole point of the agentic redesign)
- Credit check: Experian FICO Auto Score 8 ≈ **595 (Subprime)**, no
  bankruptcy/liens, ~5 inquiries in the last 6 months, ~$237/mo existing debt
- Identity check: LexisNexis, all fields matched, no fraud flag

Note the new `ApplicationId` returned (format `APP-XXXXXXXX`) — you'll need
it for the next steps.

## 3. Record supporting documents

Without documents on file, the orchestrator's underwriting stage will
correctly **escalate** ("no documents on file") instead of proceeding — this
is itself a useful thing to demo on its own if you want to show the
escalation path first.

To let it proceed past that check, call `record_document_upload` three times
against your new `ApplicationId` (metadata only — no file bytes are actually
transmitted to the platform):

| fileName | documentType |
|---|---|
| `income_verification_jordan_rivera.pdf` | `ProofOfIncome` |
| `proof_of_insurance_jordan_rivera.pdf` | `ProofOfInsurance` |
| `identity_verification_summary_jordan_rivera.pdf` | `ProofOfIdentity` |

The three actual PDF files (same folder) are cosmetic — the orchestrator's
document check just needs `get_application_documents` to return a non-empty
list. They're kept mainly so the demo has something tangible to show if
someone asks "what documents did you feed it."

## 4. Run the orchestrator

```powershell
cd C:\Users\tienv\Desktop\Tien_AgenticLoanPlatform\Services\OrchestratorService
dotnet run -- <YourNewApplicationId>
```

With documents on file, expect it to proceed into the underwriting stage for
real this time: pulling the credit check / risk score, calling
`evaluate_application`, and reaching a decision. Because the score is
subprime, this is a good scenario to demonstrate either outcome depending on
how the risk engine's thresholds are tuned:

- **Denied** → orchestrator stops, reports the decision, nothing further
  happens (no funding).
- **Approved** → orchestrator proceeds to the funding stage, prints
  `READY TO FUND: ...`, and **stops for your explicit "yes" confirmation**
  in the console before it ever calls `fund_loan` — the human-in-the-loop
  safety gate at the center of this whole design.

## 5. Files in this bundle

- `jordan_rivera_application_payload.json` — the exact `submit_application`
  arguments
- `income_verification_jordan_rivera.pdf`
- `proof_of_insurance_jordan_rivera.pdf`
- `identity_verification_summary_jordan_rivera.pdf`
- `demo_runbook.md` — this file

All four data/document files carry the same fictional test data; none of it
represents a real person, employer, or insurer.
