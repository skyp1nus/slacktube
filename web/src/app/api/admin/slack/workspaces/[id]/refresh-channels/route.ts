import { NextResponse } from "next/server";
import { backendPost, hasSession } from "@/lib/backend";

export async function POST(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  if (!(await hasSession())) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  const { id } = await params;
  const res = await backendPost(`/api/admin/slack/workspaces/${id}/refresh-channels`);
  return NextResponse.json(await res.json().catch(() => ({})), { status: res.status });
}
