import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { UploadSettingsForm } from "./upload-settings-form";

export const dynamic = "force-dynamic";

type Status = {
  slackConfigured: boolean;
  workspaceCount: number;
  google: { connected: boolean; accountCount?: number };
  quota: { usedUnits: number; capUnits: number; remainingUploads: number; totalUploads: number };
};

export default async function SettingsPage() {
  await requireSession();
  const admin = process.env.ADMIN_USER ?? "admin";
  const status = await backendGet<Status>("/api/admin/status").catch(() => null);

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <header>
        <h2 className="text-2xl font-semibold tracking-tight">Settings</h2>
        <p className="text-sm text-muted-foreground">Admin account + backend configuration.</p>
      </header>

      <UploadSettingsForm />

      <Card>
        <CardHeader>
          <CardTitle>Admin</CardTitle>
          <CardDescription>Single-admin login, configured via environment variables.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-1 text-sm">
          <div>
            <span className="text-muted-foreground">User: </span>
            {admin}
          </div>
          <div>
            <span className="text-muted-foreground">Backend: </span>
            <code>{backendBaseUrl()}</code>
          </div>
          <p className="pt-1 text-xs text-muted-foreground">Sign out from the account menu (top-right).</p>
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Connections</CardTitle>
          <CardDescription>Manage these in the Slack, Accounts, and Mapping tabs.</CardDescription>
        </CardHeader>
        <CardContent className="space-y-2 text-sm">
          <div className="flex items-center gap-2">
            <span className="w-32 text-muted-foreground">Slack</span>
            {status && status.workspaceCount > 0 ? (
              <Badge>{status.workspaceCount} workspace{status.workspaceCount === 1 ? "" : "s"}</Badge>
            ) : (
              <Badge variant="outline">none</Badge>
            )}
          </div>
          <div className="flex items-center gap-2">
            <span className="w-32 text-muted-foreground">Google accounts</span>
            {status?.google.connected ? (
              <Badge>{status.google.accountCount ?? 1} account{(status.google.accountCount ?? 1) === 1 ? "" : "s"}</Badge>
            ) : (
              <Badge variant="outline">none</Badge>
            )}
          </div>
          <div className="flex items-center gap-2">
            <span className="w-32 text-muted-foreground">Quota today (PT)</span>
            {status ? (
              <Badge variant="outline">
                {status.quota.usedUnits.toLocaleString()} / {status.quota.capUnits.toLocaleString()} units used
              </Badge>
            ) : (
              "—"
            )}
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
