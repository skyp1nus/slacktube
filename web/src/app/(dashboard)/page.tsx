import Link from "next/link";
import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import { Card, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";

export const dynamic = "force-dynamic";

type Status = {
  slackConfigured: boolean;
  workspaceCount: number;
  google: { connected: boolean; accountCount?: number };
  quota: { remainingUploads: number; totalUploads: number };
};

export default async function DashboardPage() {
  await requireSession();
  const status = await backendGet<Status>("/api/admin/status").catch(() => null);

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <header>
        <h2 className="text-2xl font-semibold tracking-tight">Dashboard</h2>
        <p className="text-sm text-muted-foreground">Slack → YouTube upload automation overview.</p>
      </header>

      {status === null && (
        <Card className="border-destructive">
          <CardHeader>
            <CardTitle className="text-destructive">Backend unreachable</CardTitle>
            <CardDescription>
              Could not reach the backend at <code>{backendBaseUrl()}</code>.
            </CardDescription>
          </CardHeader>
        </Card>
      )}

      <div className="grid gap-4 sm:grid-cols-3">
        <Link href="/slack">
          <Card className="transition-colors hover:bg-muted/50">
            <CardHeader className="pb-2">
              <CardDescription>Slack</CardDescription>
              <CardTitle className="text-base">
                {status && status.workspaceCount > 0 ? (
                  <Badge>
                    {status.workspaceCount} workspace{status.workspaceCount === 1 ? "" : "s"}
                  </Badge>
                ) : (
                  <Badge variant="outline">Not connected</Badge>
                )}
              </CardTitle>
            </CardHeader>
          </Card>
        </Link>
        <Link href="/accounts">
          <Card className="transition-colors hover:bg-muted/50">
            <CardHeader className="pb-2">
              <CardDescription>Google accounts</CardDescription>
              <CardTitle className="text-base">
                {status?.google.connected ? (
                  <Badge>Connected</Badge>
                ) : (
                  <Badge variant="outline">Not connected</Badge>
                )}
              </CardTitle>
            </CardHeader>
          </Card>
        </Link>
        <Link href="/jobs">
          <Card className="transition-colors hover:bg-muted/50">
            <CardHeader className="pb-2">
              <CardDescription>Quota today (PT)</CardDescription>
              <CardTitle className="text-base">
                {status ? `${status.quota.remainingUploads}/${status.quota.totalUploads} uploads left` : "—"}
              </CardTitle>
            </CardHeader>
          </Card>
        </Link>
      </div>
    </div>
  );
}
