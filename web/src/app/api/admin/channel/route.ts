import { NextResponse } from "next/server";
import { backendPost, hasSession } from "@/lib/backend";

export async function POST(request: Request) {
  if (!(await hasSession())) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  const body = await request.json();
  const res = await backendPost("/api/admin/channel", body);
  return NextResponse.json(await res.json().catch(() => ({})), { status: res.status });
}
