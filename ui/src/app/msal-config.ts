import { PublicClientApplication, Configuration } from '@azure/msal-browser';

/**
 * Real Entra ID SPA sign-in config, using the same app registration as
 * the MCP server (Services/McpServer) — but a different flow: this uses
 * Authorization Code + PKCE via loginRedirect, representing an actual
 * signed-in employee, not the MCP server's app-only client-credentials
 * token. Client ID and Tenant ID are identifiers, not secrets — same
 * ones already baked into each service's appsettings.json.
 */
const CLIENT_ID = '8af584e0-5ff0-461d-8ded-14f2676f83f9';
const TENANT_ID = 'c9cd754e-fe69-430a-9d84-600a461107f1';

// The delegated scope created under "Expose an API" — this is what
// shows up as the consent prompt ("Access the loan platform") the first
// time someone signs in.
export const API_SCOPE = `api://${CLIENT_ID}/access_as_user`;

const msalConfig: Configuration = {
  auth: {
    clientId: CLIENT_ID,
    authority: `https://login.microsoftonline.com/${TENANT_ID}`,
    // AZURE MIGRATION FIX: was hardcoded to 'http://localhost:4200'. That
    // baked the local dev URL into the production JS bundle, so every
    // deployed environment's login would authenticate correctly but then
    // get handed back to whatever's running on the *browser's own*
    // localhost:4200 instead of back to the actual app - MSAL's redirect
    // response landing on a local dev server if one happened to be
    // running, or failing to connect if not. window.location.origin is
    // evaluated at runtime in the browser, so it's always the origin the
    // app is actually being served from - localhost:4200 under `ng
    // serve`, the real https://<app>.<region>.azurecontainerapps.io URL
    // once deployed - with no build-time config needed. Whatever this
    // resolves to must also be listed as a valid redirect URI on the
    // Entra ID app registration (Azure Portal -> App registrations ->
    // this app -> Authentication), or Microsoft will reject the redirect
    // outright with an AADSTS50011 error page instead of silently
    // misdirecting it like this did.
    redirectUri: window.location.origin,
  },
  cache: {
    // sessionStorage (not localStorage) means signing out of the browser
    // tab clears the session — a reasonable default for an internal
    // loan-platform dashboard. Switch to localStorage if you want sign-in
    // to persist across tabs/browser restarts.
    cacheLocation: 'sessionStorage',
  },
};

export const msalInstance = new PublicClientApplication(msalConfig);
