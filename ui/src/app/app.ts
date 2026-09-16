import { Component, signal, ChangeDetectorRef } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { InteractionRequiredAuthError } from '@azure/msal-browser';
import { msalInstance, API_SCOPE } from './msal-config';
import { SERVICE_URLS } from './service-config';


/**
 * Loan Platform Dashboard — a tabbed view across all four microservices.
 * Each tab calls its own service directly on its own port (5100-5103),
 * mirroring the reality that in a real deployment these would be four
 * separately deployed APIs with no shared backend for the UI to go through.
 *
 * All four services now require a Bearer token (see Common/Auth in the
 * backend) — logging in once, at any single service, returns a token
 * valid at all four (they share one signing secret), so this UI only
 * needs one login screen, not four.
 */

interface LoanApplication {
  applicationId: string;
  customerId: string;
  applicant: { firstName: string; lastName: string; email: string; phone: string; city: string; state: string };
  employment: { employerName: string; jobTitle: string; monthlyIncome: number };
  vehicle: { year: number; make: string; model: string; vin: string; salePrice: number };
  requestedAmount: number;
  termMonths: number;
  channel: string;
  status: string;
}

interface UnderwritingDecision {
  applicationId: string;
  approved: boolean;
  reason: string;
  approvedAmount: number | null;
  interestRate: number | null;
  debtToIncomeRatio: number;
  loanToValueRatio: number;
  stipulations: string[];
  decidedAt: string;
}

interface Funding {
  applicationId: string;
  loanId: string;
  fundedAmount: number;
  interestRate: number;
  termMonths: number;
  disbursementMethod: string;
  fundedAt: string;
}

interface Loan {
  loanId: string;
  accountNumber: string;
  applicationId: string;
  principalAmount: number;
  interestRate: number;
  termMonths: number;
  paymentMethod: string;
  currentBalance: number;
  originatedAt: string;
}

// Matches Common/Logging/TraceEntry.cs (TraceBuffer.cs) — one entry per
// TraceLogger.Trace() call, served by each service's /api/trace/recent.
interface TraceEntry {
  seq: number;
  timestamp: string;
  service: string;
  applicationId: string | null;
  step: string;
  message: string;
  data: any;
}

// UI REDESIGN: replaced the old four-tab layout (Origination/Underwriting/
// Funding/Servicing as separate, disconnected tables that only shared an
// applicationId a viewer had to cross-reference by eye) with one unified
// Applications list. Each row joins that application's data from all four
// services by applicationId and can expand to show its full journey in one
// place, with a stage stepper showing exactly where it stands.
interface ApplicationRow {
  application: LoanApplication;
  decision: UnderwritingDecision | null;
  funding: Funding | null;
  loan: Loan | null;
}

type StageStatus = 'done' | 'current' | 'denied' | 'blocked' | 'pending';

// AZURE MIGRATION CHANGE: SERVICE_URLS used to be hardcoded here as
// localhost:5100-5103, which only worked because the browser and the
// containers shared one machine. It now lives in ./service-config.ts,
// populated at app startup (see main.ts) from /config.json — a file
// generated at container start (see ui/docker-entrypoint.sh) from each
// backend's real deployed URL. Every SERVICE_URLS.* reference below is
// unchanged; only where the values come from changed.

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  constructor(private http: HttpClient, private cdr: ChangeDetectorRef) {
  void this.restoreSession();
}

  private async restoreSession() {
  await msalInstance.initialize();
  console.log('[auth] restoreSession starting, url:', window.location.href);

  const redirectResult = await msalInstance.handleRedirectPromise();
  console.log('[auth] handleRedirectPromise result:', redirectResult);
  if (redirectResult) {
    this.token = redirectResult.accessToken;
    console.log('[auth] token set from redirect, length:', this.token.length);
    await this.refreshAll();
    this.cdr.detectChanges()
    console.log('[auth] refreshAll done, token still set:', !!this.token, 'error signal:', this.error());
    return;
  }

  const accounts = msalInstance.getAllAccounts();
  console.log('[auth] no redirect result, accounts found:', accounts.length);
  if (accounts.length === 0) return;

  try {
    const result = await msalInstance.acquireTokenSilent({
      scopes: [API_SCOPE],
      account: accounts[0],
    });
    this.token = result.accessToken;
    console.log('[auth] silent token acquired, length:', this.token.length);
    await this.refreshAll();
  } catch (e) {
    console.error('[auth] silent token acquisition failed:', e);
  }
}

  // --- Auth state ---
  token = '';
  authError = signal('');
  authLoading = signal(false);

  get isLoggedIn(): boolean {
    return !!this.token;
  }

  private authHeaders() {
    return { headers: { Authorization: `Bearer ${this.token}` } };
  }

  async login() {
    this.authLoading.set(true);
    this.authError.set('');
    try {
      await msalInstance.initialize();
      // loginRedirect navigates the whole page to Microsoft's real
      // sign-in page, then back to this app afterward — more robust than
      // loginPopup, which can hit browser popup-blocking edge cases
      // (see https://aka.ms/msal.js.errors#block_nested_popups).
      // This call navigates away; nothing after it in this method runs
      // until the browser comes back and restoreSession() picks up the
      // result via handleRedirectPromise() above.
      await msalInstance.loginRedirect({ scopes: [API_SCOPE] });
    } catch (e: any) {
      this.authError.set(e?.errorMessage || e?.message || 'Sign-in failed.');
      this.authLoading.set(false);
    }
  }

  async logout() {
    this.token = '';
    this.stopLogsPolling();
    const account = msalInstance.getAllAccounts()[0];
    if (account) {
      await msalInstance.logoutRedirect({ account });
    }
  }

  /**
   * Gets a fresh access token before a wave of API calls, renewing
   * silently if the cached one is close to expiring (Entra ID tokens are
   * typically valid ~1 hour). Falls back to nothing if silent renewal
   * needs interaction — the existing handleAuthFailure() 401 handling
   * covers that case by prompting the user to sign in again.
   */
  private async ensureFreshToken() {
    const account = msalInstance.getAllAccounts()[0];
    if (!account) return;
    try {
      const result = await msalInstance.acquireTokenSilent({ scopes: [API_SCOPE], account });
      this.token = result.accessToken;
    } catch (e) {
      if (e instanceof InteractionRequiredAuthError) {
        // Needs a real sign-in prompt — handled by handleAuthFailure()
        // on the next 401 rather than popping up unprompted here.
      }
    }
  }

  // --- Dashboard state ---
  applications: LoanApplication[] = [];
  decisions: UnderwritingDecision[] = [];
  fundings: Funding[] = [];
  loans: Loan[] = [];

  error = signal('');
  serviceStatus = signal<Record<string, boolean>>({
    origination: false, underwriting: false, funding: false, servicing: false,
  });

  // --- Tabs ---
  // The dashboard used to be one unified Applications view with no tabs at
  // all (see the UI REDESIGN comment on ApplicationRow above, which
  // deliberately replaced the old four-tab layout). This adds exactly one
  // tab back — Logs — rather than reintroducing the old per-service split;
  // Applications stays the default, single unified view.
  activeTab = signal<'applications' | 'logs'>('applications');

  setActiveTab(tab: 'applications' | 'logs') {
    this.activeTab.set(tab);
    if (tab === 'logs') this.startLogsPolling();
    else this.stopLogsPolling();
  }

  // --- Logs tab: live trace across all four services ---
  // "Real time" here means polling every second, not a server push
  // (SSE/WebSocket) — a native browser EventSource can't attach the
  // Authorization bearer header this whole app already authenticates with
  // everywhere else, and introducing a second, differently-authenticated
  // streaming mechanism just for this one tab wasn't worth it against
  // 1-second polling being indistinguishable from push for a human reading
  // logs. Each poll only asks for entries newer than the last one it saw
  // (the `since` cursor below, keyed by TraceEntry.Seq — see
  // TraceController.Recent on the backend), so steady-state polling is
  // cheap regardless of how long the tab has been open.
  readonly logServiceKeys: (keyof typeof SERVICE_URLS)[] = ['origination', 'underwriting', 'funding', 'servicing'];
  logEntries = signal<TraceEntry[]>([]);
  logsLive = signal(true);
  logFilter = '';
  private logCursors: Record<string, number> = { origination: 0, underwriting: 0, funding: 0, servicing: 0 };
  private logsPollHandle: ReturnType<typeof setInterval> | null = null;
  private readonly LOG_BUFFER_LIMIT = 1000;
  private readonly LOG_POLL_INTERVAL_MS = 1000;

  private startLogsPolling() {
    if (this.logsPollHandle) return; // already running
    void this.pollLogsOnce(); // don't wait a full second for the first entries to show up
    this.logsPollHandle = setInterval(() => {
      if (this.logsLive()) void this.pollLogsOnce();
    }, this.LOG_POLL_INTERVAL_MS);
  }

  private stopLogsPolling() {
    if (this.logsPollHandle) {
      clearInterval(this.logsPollHandle);
      this.logsPollHandle = null;
    }
  }

  /** Pauses/resumes polling in place — cursors are kept, so resuming catches up on whatever was missed rather than losing it. */
  toggleLogsLive() {
    this.logsLive.update((v) => !v);
  }

  clearLogs() {
    this.logEntries.set([]);
  }

  private async fetchLogsFor(service: keyof typeof SERVICE_URLS): Promise<TraceEntry[]> {
    try {
      const since = this.logCursors[service] ?? 0;
      const entries = await firstValueFrom(
        this.http.get<TraceEntry[]>(`${SERVICE_URLS[service]}/api/trace/recent?since=${since}`, this.authHeaders())
      );
      if (entries.length > 0) {
        this.logCursors[service] = entries[entries.length - 1].seq;
      }
      return entries;
    } catch {
      // One service being unreachable shouldn't blank out the other
      // three's logs — same "degrade, don't crash" pattern as checkHealth().
      return [];
    }
  }

  private async pollLogsOnce() {
    if (!this.token) return;
    const results = await Promise.all(this.logServiceKeys.map((service) => this.fetchLogsFor(service)));
    const newEntries = results.flat();
    if (newEntries.length === 0) return;
    this.logEntries.update((current) => {
      // Each service has its own independent Seq counter, so Seq alone
      // can't order across services — Timestamp (all UTC, ISO 8601) can.
      const merged = [...current, ...newEntries].sort((a, b) => a.timestamp.localeCompare(b.timestamp));
      return merged.length > this.LOG_BUFFER_LIMIT ? merged.slice(merged.length - this.LOG_BUFFER_LIMIT) : merged;
    });
  }

  /** Newest first (matches "tail -f" expectations) and optionally narrowed to one applicationId. */
  get filteredLogEntries(): TraceEntry[] {
    const filter = this.logFilter.trim().toUpperCase();
    const entries = filter
      ? this.logEntries().filter((e) => (e.applicationId ?? '').toUpperCase().includes(filter))
      : this.logEntries();
    return [...entries].reverse();
  }

  newApp = {
    customerId: 'CUST-' + Math.random().toString(36).slice(2, 8).toUpperCase(),
    applicant: {
      firstName: '',
      lastName: '',
      dateOfBirth: '1990-01-01',
      ssnLastFour: '',
      email: '',
      phone: '',
      addressLine1: '',
      city: '',
      state: '',
      zipCode: '',
    },
    employment: {
      employerName: '',
      jobTitle: '',
      monthlyIncome: 5000,
      employmentMonths: 24,
    },
    vehicle: {
      year: new Date().getFullYear(),
      make: '',
      model: '',
      vin: '',
      mileage: 0,
      condition: 'Used',
      salePrice: 25000,
    },
    requestedAmount: 20000,
    downPayment: 2000,
    termMonths: 60,
    channel: 'Online',
    dealerName: null as string | null,
  };
  submitting = signal(false);
  submitResult = signal<any>(null);

  // Starts closed — the form used to always be open above the applications
  // list, so checking on existing applications meant scrolling past 20+
  // fields every time. Opens on demand, and auto-closes after a
  // successful submit (see submitApplication()).
  showNewApplicationForm = signal(false);

  toggleNewApplicationForm() {
    this.showNewApplicationForm.update((v) => !v);
  }

  // Which application's row is currently expanded to show its full detail.
  // Only one at a time, accordion-style, to keep the list scannable.
  expandedApplicationId = signal<string | null>(null);
  readonly stageLabels = ['Submitted', 'Underwriting', 'Funding', 'Servicing'];

  toggleExpand(applicationId: string) {
    const next = this.expandedApplicationId() === applicationId ? null : applicationId;
    this.expandedApplicationId.set(next);
    // The document-extraction tool lives inside the expanded row now
    // (it used to be a standalone card with its own "pick an application"
    // dropdown) - scope it to whichever application is open, and clear
    // any leftover extraction state from a previously-open row.
    this.extractApplicationId = next ?? '';
    this.discardExtraction();
  }

  // BUG FIX (2026-09-14): applicationRows used to be a plain getter that
  // built a brand-new array (and brand-new row objects) on every single
  // call. The template reads it from both *ngIf and *ngFor, so it was
  // being re-evaluated on every change-detection pass — and because it
  // never returned the same array/object references twice, Angular (now
  // running zoneless, see app.config.ts) kept seeing "new" data every
  // time, which kept scheduling yet another check, forever. That runaway
  // loop is what showed up as hundreds of
  // `[Violation] 'requestAnimationFrame' handler took Nms` messages in the
  // console, and by keeping the main thread perpetually busy re-rendering
  // unchanged data, it starved out real UI updates like a row's own
  // expand/collapse click.
  //
  // Fixed by caching the joined rows and only recomputing them when the
  // underlying data actually changes (see updateApplicationRows(), called
  // once at the end of refreshAll()) instead of on every template read.
  private _applicationRows: ApplicationRow[] = [];

  /** Joins applications with their decision/funding/loan by applicationId, so one row can show an application's whole journey instead of splitting it across four separate tables. Cached — see updateApplicationRows(). */
  get applicationRows(): ApplicationRow[] {
    return this._applicationRows;
  }

  private updateApplicationRows(): void {
    this._applicationRows = this.applications.map((application) => ({
      application,
      decision: this.decisions.find((d) => d.applicationId === application.applicationId) ?? null,
      funding: this.fundings.find((f) => f.applicationId === application.applicationId) ?? null,
      loan: this.loans.find((l) => l.applicationId === application.applicationId) ?? null,
    }));
  }

  get approvedCount(): number {
    return this.decisions.filter((d) => d.approved).length;
  }

  get deniedCount(): number {
    return this.decisions.filter((d) => !d.approved).length;
  }

  /**
   * Status for one stage (0=Submitted, 1=Underwriting, 2=Funding,
   * 3=Servicing) of one application's stepper. 'blocked' means a denial
   * upstream means this stage will never happen, as distinct from
   * 'pending' (still on track, just not reached yet).
   */
  stageStatus(row: ApplicationRow, stage: number): StageStatus {
    const denied = row.decision != null && !row.decision.approved;
    switch (stage) {
      case 0:
        return 'done'; // the row only exists because the application was submitted
      case 1:
        if (row.decision == null) return 'current';
        return row.decision.approved ? 'done' : 'denied';
      case 2:
        if (denied) return 'blocked';
        if (row.funding != null) return 'done';
        return row.decision?.approved ? 'current' : 'pending';
      case 3:
      default:
        if (denied) return 'blocked';
        if (row.loan != null) return 'done';
        return row.funding != null ? 'current' : 'pending';
    }
  }

  async refreshAll() {
    await Promise.all([
      this.checkHealth('origination'),
      this.checkHealth('underwriting'),
      this.checkHealth('funding'),
      this.checkHealth('servicing'),
    ]);
    await this.loadApplications();
    await this.loadFundings();
    await this.loadLoans();
    await this.loadAllDecisions();
    // Recompute the cached joined rows once, now that all four loads have
    // finished — see updateApplicationRows() for why this replaced the old
    // per-render getter.
    this.updateApplicationRows();
  }

  // Health checks stay unauthenticated — /api/health is deliberately
  // public on every service, so the status pills work even before login.
  private async checkHealth(service: keyof typeof SERVICE_URLS) {
    try {
      await firstValueFrom(this.http.get(`${SERVICE_URLS[service]}/api/health`));
      this.serviceStatus.update((s) => ({ ...s, [service]: true }));
    } catch {
      this.serviceStatus.update((s) => ({ ...s, [service]: false }));
    }
  }

  private handleAuthFailure(e: any) {
    if (e?.status === 401) {
      this.token = '';
      this.error.set('Session expired. Please sign in again.');
    }
  }

  async loadApplications() {
    if (!this.token) return;
    try {
      this.applications = await firstValueFrom(
        this.http.get<LoanApplication[]>(`${SERVICE_URLS.origination}/api/applications`, this.authHeaders())
      );
    } catch (e) {
      this.applications = [];
      this.handleAuthFailure(e);
    }
  }

  async loadFundings() {
    if (!this.token) return;
    try {
      this.fundings = await firstValueFrom(
        this.http.get<Funding[]>(`${SERVICE_URLS.funding}/api/fundings`, this.authHeaders())
      );
    } catch (e) {
      this.fundings = [];
      this.handleAuthFailure(e);
    }
  }

  async loadLoans() {
    if (!this.token) return;
    try {
      this.loans = await firstValueFrom(
        this.http.get<Loan[]>(`${SERVICE_URLS.servicing}/api/loans`, this.authHeaders())
      );
    } catch (e) {
      this.loans = [];
      this.handleAuthFailure(e);
    }
  }

  private async loadDecisionFor(applicationId: string): Promise<UnderwritingDecision | null> {
    try {
      return await firstValueFrom(
        this.http.get<UnderwritingDecision>(`${SERVICE_URLS.underwriting}/api/underwriting/${applicationId}/decision`, this.authHeaders())
      );
    } catch {
      return null;
    }
  }

  // --- Underwriter co-pilot (Claude) ---
  // Keyed by applicationId rather than kept on ApplicationRow itself:
  // updateApplicationRows() rebuilds every row from scratch on each
  // refresh (see that method), which would silently wipe any co-pilot
  // state stored directly on a row object. A separate map survives a
  // refresh untouched.
  //
  // BUG FIX: this was a plain mutable Record, updated in place, on the
  // theory that a template read of a plain object field would just pick up
  // the latest value. That's wrong in THIS app specifically —
  // app.config.ts turns on provideZonelessChangeDetection() with no
  // zone.js polyfill at all, so nothing automatically re-checks the
  // template after an `await` resumes; only a signal write, a DOM event
  // firing, or an explicit ChangeDetectorRef.detectChanges() call schedules
  // a re-render (see that file's own BUG FIX comment for the identical
  // failure mode on row-expand clicks). The symptom this produced: the
  // Anthropic call visibly succeeded (real entries in
  // platform.claude.com/logs) and the backend presumably returned fine,
  // but the panel never showed anything — the state was updated in memory,
  // Angular just never knew to look again. Every other piece of async UI
  // state in this file (extracting, extractedDocument, authLoading, etc.)
  // is already a signal() for exactly this reason; this was the one
  // inconsistent piece. Now a signal, same as the rest.
  //
  // Deliberately manual — there is no automatic call anywhere in this
  // file that populates this map. It's only ever written to by a click on
  // "Generate summary" / "Regenerate" below, exactly so the Anthropic API
  // is never charged for an application nobody actually opened and asked
  // about (see UnderwritingController.GetCopilotSummary's caching for the
  // matching backend half of this).
  copilotState = signal<Record<string, {
    loading: boolean;
    error: string;
    summary: { summary: string; riskFactors: string[]; inconsistencies: string[]; suggestedStipulations: string[] } | null;
    generatedAt: string | null;
    cached: boolean;
  }>>({});

  private patchCopilotState(applicationId: string, patch: Partial<{
    loading: boolean;
    error: string;
    summary: { summary: string; riskFactors: string[]; inconsistencies: string[]; suggestedStipulations: string[] } | null;
    generatedAt: string | null;
    cached: boolean;
  }>) {
    this.copilotState.update((current) => {
      const existing = current[applicationId] ?? { loading: false, error: '', summary: null, generatedAt: null, cached: false };
      return { ...current, [applicationId]: { ...existing, ...patch } };
    });
  }

  async generateCopilotSummary(applicationId: string, regenerate = false) {
    this.patchCopilotState(applicationId, { loading: true, error: '' });
    try {
      const url = `${SERVICE_URLS.underwriting}/api/underwriting/${applicationId}/copilot-summary${regenerate ? '?regenerate=true' : ''}`;
      // POST with no body — this is a manual action-trigger endpoint, not
      // a resource creation, but POST (not GET) is the right verb since a
      // non-cached call does real, non-idempotent work (an Anthropic API
      // call with a real cost). See UnderwritingController.GetCopilotSummary.
      const result: any = await firstValueFrom(this.http.post(url, null, this.authHeaders()));
      this.patchCopilotState(applicationId, {
        loading: false,
        summary: result.summary,
        generatedAt: result.generatedAt,
        cached: result.cached,
      });
    } catch (e: any) {
      this.patchCopilotState(applicationId, {
        loading: false,
        error: e?.error?.error || e?.message || 'Could not generate a summary.',
      });
      this.handleAuthFailure(e);
    }
  }

  async loadAllDecisions() {
    if (!this.token) return;
    const results = await Promise.all(
      this.applications.map((a) => this.loadDecisionFor(a.applicationId))
    );
    this.decisions = results.filter((d): d is UnderwritingDecision => d !== null);
  }

  // --- Document extraction (Claude) state ---
  extractApplicationId = '';
  extractDocumentType: 'pay_stub' | 'w2' | 'bank_statement' | 'id_document' = 'pay_stub';
  selectedFile: File | null = null;
  extracting = signal(false);
  extractionError = signal('');
  extractedDocument = signal<any>(null); // { applicationId, fileName, documentType, extractedData }
  editableFields: Record<string, any> = {};
  savingDocument = signal(false);
  documentSaved = signal(false);

  onFileSelected(event: Event) {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] ?? null;
  }

  /** camelCase -> "Camel Case", for rendering field labels without a hardcoded form per document type. */
  humanizeKey(key: string): string {
    return key.replace(/([A-Z])/g, ' $1').replace(/^./, (s) => s.toUpperCase()).trim();
  }

  get editableFieldKeys(): string[] {
    return Object.keys(this.editableFields).filter((k) => k !== 'confidenceFlags');
  }

  async extractDocument() {
    if (!this.extractApplicationId || !this.selectedFile) {
      this.extractionError.set('Pick an application and choose a file first.');
      return;
    }
    this.extracting.set(true);
    this.extractionError.set('');
    this.extractedDocument.set(null);
    this.documentSaved.set(false);
    try {
      const formData = new FormData();
      formData.append('file', this.selectedFile);
      formData.append('documentType', this.extractDocumentType);
      // Note: don't set a Content-Type header manually here — the
      // browser needs to set multipart/form-data with its own boundary,
      // which authHeaders() doesn't interfere with (it only sets
      // Authorization).
      const result: any = await firstValueFrom(
        this.http.post(
          `${SERVICE_URLS.origination}/api/applications/${this.extractApplicationId}/documents/extract`,
          formData,
          this.authHeaders()
        )
      );
      this.extractedDocument.set(result);
      // Copy into a plain editable object so ngModel can bind to
      // individual fields for correction before saving.
      this.editableFields = { ...result.extractedData };
    } catch (e: any) {
      this.extractionError.set(e?.error?.error || e?.message || 'Extraction failed.');
      this.handleAuthFailure(e);
    } finally {
      this.extracting.set(false);
    }
  }

  async confirmAndSaveDocument() {
    const doc = this.extractedDocument();
    if (!doc) return;
    this.savingDocument.set(true);
    this.extractionError.set('');
    try {
      await firstValueFrom(
        this.http.post(
          `${SERVICE_URLS.origination}/api/applications/${this.extractApplicationId}/documents`,
          {
            fileName: doc.fileName,
            documentType: doc.documentType,
            extractedDataJson: JSON.stringify(this.editableFields),
          },
          this.authHeaders()
        )
      );
      this.documentSaved.set(true);
    } catch (e: any) {
      this.extractionError.set(e?.error?.error || e?.message || 'Failed to save document.');
      this.handleAuthFailure(e);
    } finally {
      this.savingDocument.set(false);
    }
  }

  discardExtraction() {
    this.extractedDocument.set(null);
    this.editableFields = {};
    this.selectedFile = null;
    this.documentSaved.set(false);
    this.extractionError.set('');
  }

  async submitApplication() {
    this.submitting.set(true);
    this.submitResult.set(null);
    this.error.set('');
    try {
      const result = await firstValueFrom(
        this.http.post(`${SERVICE_URLS.origination}/api/applications`, this.newApp, this.authHeaders())
      );
      this.submitResult.set(result);
      // Give the event chain a moment to propagate through Underwriting,
      // Funding, and Servicing before refreshing the applications list.
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await this.refreshAll();
      // Collapse the form and jump straight to the new application's row,
      // expanded, so its journey is immediately visible instead of asking
      // the person to go find it in the list themselves.
      const newApplicationId = (result as any).applicationId;
      this.showNewApplicationForm.set(false);
      this.expandedApplicationId.set(newApplicationId);
      this.extractApplicationId = newApplicationId;
    } catch (e: any) {
      this.error.set(e?.error?.error || e?.message || 'Failed to submit application.');
      this.handleAuthFailure(e);
    } finally {
      this.submitting.set(false);
    }
  }
}
