import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { sessionToken } from "@/lib/session";

export async function POST(request: Request) {
  const { username, password } = await request.json().catch(() => ({}) as Record<string, string>);
  if (username !== process.env.ADMIN_USER || password !== process.env.ADMIN_PASSWORD) {
    return NextResponse.json({ error: "Invalid credentials" }, { status: 401 });
  }
  const c = await cookies();
  c.set("session", sessionToken(), {
    httpOnly: true,
    sameSite: "lax",
    // Secure must be OFF for plain http://localhost (browsers drop Secure cookies over http).
    // Set COOKIE_SECURE=true only when serving the panel over HTTPS.
    secure: process.env.COOKIE_SECURE === "true",
    path: "/",
    maxAge: 60 * 60 * 8,
  });
  return NextResponse.json({ ok: true });
}
