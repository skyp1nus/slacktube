import { NextRequest, NextResponse } from "next/server";
import { hasSession } from "@/lib/backend";

const BASE = process.env.BACKEND_URL ?? "http://localhost:5080";
const TOKEN = process.env.BACKEND_ADMIN_TOKEN ?? "";

/**
 * BFF proxy: forwards every /api/admin/* call from the browser to the backend with the
 * server-only X-Admin-Token, after checking the admin session. Keeps the token off the client.
 */
async function proxy(req: NextRequest, path: string[]): Promise<NextResponse> {
  if (!(await hasSession())) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });

  const url = `${BASE}/api/admin/${path.join("/")}${req.nextUrl.search}`;
  const init: RequestInit = {
    method: req.method,
    headers: { "X-Admin-Token": TOKEN },
    cache: "no-store",
  };

  if (req.method !== "GET" && req.method !== "DELETE" && req.method !== "HEAD") {
    const body = await req.text();
    if (body) {
      init.body = body;
      (init.headers as Record<string, string>)["Content-Type"] =
        req.headers.get("content-type") ?? "application/json";
    }
  }

  try {
    const res = await fetch(url, init);
    const text = await res.text();
    return new NextResponse(text || null, {
      status: res.status,
      headers: { "Content-Type": res.headers.get("content-type") ?? "application/json" },
    });
  } catch {
    return NextResponse.json({ error: "Backend unreachable" }, { status: 502 });
  }
}

type Ctx = { params: Promise<{ path: string[] }> };

export async function GET(req: NextRequest, { params }: Ctx) {
  return proxy(req, (await params).path);
}
export async function POST(req: NextRequest, { params }: Ctx) {
  return proxy(req, (await params).path);
}
export async function PATCH(req: NextRequest, { params }: Ctx) {
  return proxy(req, (await params).path);
}
export async function DELETE(req: NextRequest, { params }: Ctx) {
  return proxy(req, (await params).path);
}
