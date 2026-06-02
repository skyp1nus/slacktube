import { NextResponse } from "next/server";
import { backendGet, hasSession } from "@/lib/backend";

export async function GET() {
  if (!(await hasSession())) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  try {
    const channels = await backendGet<unknown>("/api/admin/channels");
    return NextResponse.json(channels);
  } catch {
    return NextResponse.json([], { status: 200 });
  }
}
