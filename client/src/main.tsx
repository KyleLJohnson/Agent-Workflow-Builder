import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { ErrorBoundary } from "react-error-boundary";
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";
import App from "@/App";
import ErrorFallback from "@/components/ErrorFallback";
import { msalConfig, isAuthConfigured } from "@/authConfig";
import { setMsalInstance } from "@/api";
import { setAuthMsalInstance } from "@/authUtils";
import "@/styles/global.css";

const msalInstance = new PublicClientApplication(msalConfig);

if (isAuthConfigured()) {
  setMsalInstance(msalInstance);
  setAuthMsalInstance(msalInstance);
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <ErrorBoundary FallbackComponent={ErrorFallback}>
      <MsalProvider instance={msalInstance}>
        <App />
      </MsalProvider>
    </ErrorBoundary>
  </StrictMode>
);
