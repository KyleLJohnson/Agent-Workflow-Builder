import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { PublicClientApplication } from "@azure/msal-browser";
import { MsalProvider } from "@azure/msal-react";
import App from "./App";
import { msalConfig, isAuthConfigured } from "./authConfig";
import { setMsalInstance } from "./api";
import { setAuthMsalInstance } from "./authUtils";
import "./style.css";

const msalInstance = new PublicClientApplication(msalConfig);

if (isAuthConfigured()) {
  setMsalInstance(msalInstance);
  setAuthMsalInstance(msalInstance);
}

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <MsalProvider instance={msalInstance}>
      <App />
    </MsalProvider>
  </StrictMode>
);
