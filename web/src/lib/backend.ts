import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { isValidSession } from "./session";

const BASE = process.env.BACKEND_URL ?? "http://localhost:5080";
const TOKEN = process.env.BACKEND_ADMIN_TOKEN ?? "";

/** Redirects to /login when the request has no valid admin session. */
export async function requireSession() {
  const c = await cookies();
  if (!isValidSession(c.get("session")?.value)) redirect("/login");
}

/** True when the current request carries a valid admin session (for API route guards). */
export async function hasSession() {
  const c = await cookies();
  return isValidSession(c.get("session")?.value);
}

export async function backendGet<T>(path: string): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "X-Admin-Token": TOKEN },
    cache: "no-store",
  });
  if (!res.ok) throw new Error(`Backend GET ${path} → ${res.status}`);
  return (await res.json()) as T;
}

export async function backendPost(path: string, body: unknown): Promise<Response> {
  return fetch(`${BASE}${path}`, {
    method: "POST",
    headers: { "X-Admin-Token": TOKEN, "Content-Type": "application/json" },
    body: JSON.stringify(body),
    cache: "no-store",
  });
}

/** Browser-reachable base URL of the backend (used for the Google OAuth top-level navigation). */
export function backendBaseUrl(): string {
  return process.env.NEXT_PUBLIC_BACKEND_URL ?? BASE;
}
