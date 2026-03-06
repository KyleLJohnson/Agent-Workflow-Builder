import type { IPublicClientApplication } from "@azure/msal-browser";
import { tokenRequest, isAuthConfigured } from "@/authConfig";

let msalInstance: IPublicClientApplication | null = null;

/** Called by main.tsx to provide the MSAL instance. */
export function setAuthMsalInstance(instance: IPublicClientApplication): void {
  msalInstance = instance;
}

/**
 * Acquires an access token for the SignalR connection.
 * Returns an empty string when auth is not configured (local dev).
 */
export async function acquireAccessToken(): Promise<string> {
  if (!msalInstance || !isAuthConfigured()) return "";
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) return "";
  try {
    const response = await msalInstance.acquireTokenSilent({
      ...tokenRequest,
      account: accounts[0],
    });
    return response.accessToken;
  } catch {
    return "";
  }
}
