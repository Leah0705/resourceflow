import { chromium } from "@playwright/test";
import path from "path";
import { ADMIN_EMAIL, ADMIN_PASSWORD } from "./helpers";

export const ADMIN_STATE_FILE = path.join(__dirname, ".auth/admin.json");

/**
 * Login once before all tests run and save the auth cookie to disk.
 * Admin tests load this state instead of navigating through the login form,
 * which would otherwise exhaust the 5-per-minute auth rate limit.
 */
export default async function globalSetup() {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ baseURL: "http://localhost:5062" });
  const page = await ctx.newPage();

  // Hit the login API once — sets the resourceflow_auth cookie in the context
  const res = await page.request.post("/api/admin/auth/login", {
    data: { email: ADMIN_EMAIL, password: ADMIN_PASSWORD },
  });
  if (!res.ok()) {
    throw new Error(`Admin login failed during global setup (HTTP ${res.status()})`);
  }

  // Persist cookies so every admin test can load this state
  await ctx.storageState({ path: ADMIN_STATE_FILE });
  await browser.close();
}
