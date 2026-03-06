import type { Configuration, RedirectRequest, SilentRequest } from "@azure/msal-browser";

/**
 * MSAL configuration for Microsoft Entra ID authentication.
 * Replace placeholder values with real app registration IDs before enabling auth.
 */
export const msalConfig: Configuration = {
  auth: {
    clientId: "<client-id>",
    authority: "https://login.microsoftonline.com/<tenant-id>",
    redirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: "localStorage",
    storeAuthStateInCookie: false,
  },
};

const apiScope = `api://${msalConfig.auth.clientId}/access_as_user`;

export const loginRequest: RedirectRequest = {
  scopes: [apiScope],
};

export const tokenRequest: SilentRequest = {
  scopes: [apiScope],
};

/** Returns true when auth is configured with real (non-placeholder) values. */
export function isAuthConfigured(): boolean {
  return (
    msalConfig.auth.clientId !== "<client-id>" &&
    !msalConfig.auth.authority?.includes("<tenant-id>")
  );
}
