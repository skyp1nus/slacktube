import { NextResponse } from "next/server";
import { backendDelete, hasSession } from "@/lib/backend";

export async function DELETE(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  if (!(await hasSession())) return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  const { id } = await params;
  const res = await backendDelete(`/api/admin/slack/workspaces/${id}`);
  return new NextResponse(null, { status: res.status });
}
