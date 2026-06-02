import crypto from "node:crypto";

// Signing key for the session cookie. Server-only — never shipped to the browser.
const secret =
  process.env.SESSION_SECRET || process.env.ADMIN_PASSWORD || "slacktube-dev-secret";

/** Deterministic, unguessable session value (HMAC of a fixed label with the server secret). */
export function sessionToken(): string {
  return crypto
    .createHmac("sha256", secret)
    .update("slacktube-admin-session")
    .digest("hex");
}

export function isValidSession(value?: string | null): boolean {
  if (!value) return false;
  const a = Buffer.from(value);
  const b = Buffer.from(sessionToken());
  return a.length === b.length && crypto.timingSafeEqual(a, b);
}
