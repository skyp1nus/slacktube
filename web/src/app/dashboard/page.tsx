import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { buttonVariants } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { SlackForm } from "./slack-form";
import { ChannelSelect } from "./channel-select";
import { LogoutButton } from "./logout-button";

export const dynamic = "force-dynamic";

type Status = {
  slackConfigured: boolean;
  listeningChannelId: string | null;
  google: { connected: boolean; scopes: string | null; connectedAt: string | null };
  quota: { usedUnits: number; capUnits: number; remainingUploads: number; totalUploads: number };
};

type Job = {
  id: string;
  fileName: string | null;
  state: string;
  youTubeUrl: string | null;
  error: string | null;
  tags: string[];
  createdAt: string;
  updatedAt: string;
};

type Channel = { id: string; name: string; isMember: boolean };

function stateVariant(state: string): "default" | "secondary" | "destructive" | "outline" {
  switch (state) {
    case "Done":
      return "default";
    case "Failed":
    case "Cancelled":
    case "Blocked":
      return "destructive";
    case "Queued":
      return "outline";
    default:
      return "secondary"; // Downloading / Uploading / Processing
  }
}

export default async function Dashboard() {
  await requireSession();

  const status = await backendGet<Status>("/api/admin/status").catch(() => null);
  const jobs = await backendGet<Job[]>("/api/admin/jobs").catch(() => [] as Job[]);
  const channels: Channel[] = status?.slackConfigured
    ? await backendGet<Channel[]>("/api/admin/channels").catch(() => [])
    : [];

  const backendUnreachable = status === null;

  return (
    <div className="mx-auto w-full max-w-4xl space-y-6 p-6">
      <header className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-semibold">SlackTube</h1>
          <p className="text-sm text-muted-foreground">Slack → YouTube upload automation</p>
        </div>
        <LogoutButton />
      </header>

      {backendUnreachable && (
        <Card className="border-destructive">
          <CardHeader>
            <CardTitle className="text-destructive">Backend unreachable</CardTitle>
            <CardDescription>
              Could not reach the backend at <code>{backendBaseUrl()}</code>. Make sure it is running
              and <code>BACKEND_URL</code> / <code>BACKEND_ADMIN_TOKEN</code> are set correctly.
            </CardDescription>
          </CardHeader>
        </Card>
      )}

      {/* ---- connection status ---- */}
      <div className="grid gap-4 sm:grid-cols-3">
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Slack</CardDescription>
            <CardTitle className="text-base">
              {status?.slackConfigured ? (
                <Badge>Configured</Badge>
              ) : (
                <Badge variant="outline">Not configured</Badge>
              )}
            </CardTitle>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Google (YouTube + Drive)</CardDescription>
            <CardTitle className="text-base">
              {status?.google.connected ? (
                <Badge>Connected</Badge>
              ) : (
                <Badge variant="outline">Not connected</Badge>
              )}
            </CardTitle>
          </CardHeader>
        </Card>
        <Card>
          <CardHeader className="pb-2">
            <CardDescription>Quota today (PT)</CardDescription>
            <CardTitle className="text-base">
              {status ? `${status.quota.remainingUploads}/${status.quota.totalUploads} uploads left` : "—"}
            </CardTitle>
          </CardHeader>
        </Card>
      </div>

      {/* ---- Slack credentials ---- */}
      <Card>
        <CardHeader>
          <CardTitle>Connect Slack</CardTitle>
          <CardDescription>
            Bot token + signing secret. Stored encrypted in the backend database.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <SlackForm configured={status?.slackConfigured ?? false} />
        </CardContent>
      </Card>

      {/* ---- Google OAuth ---- */}
      <Card>
        <CardHeader>
          <CardTitle>Connect Google</CardTitle>
          <CardDescription>
            One consent grants YouTube upload + Drive readonly. The refresh token is encrypted at rest.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <a className={buttonVariants()} href={`${backendBaseUrl()}/google/oauth/start`}>
            {status?.google.connected ? "Reconnect Google" : "Connect Google"}
          </a>
          {status?.google.connectedAt && (
            <p className="mt-2 text-xs text-muted-foreground">
              Connected {new Date(status.google.connectedAt).toLocaleString()}
            </p>
          )}
        </CardContent>
      </Card>

      {/* ---- Listening channel ---- */}
      <Card>
        <CardHeader>
          <CardTitle>Listening channel</CardTitle>
          <CardDescription>
            The bot reads upload templates from — and posts the live status message in — this channel.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ChannelSelect channels={channels} current={status?.listeningChannelId ?? null} />
        </CardContent>
      </Card>

      {/* ---- Job history ---- */}
      <Card>
        <CardHeader>
          <CardTitle>Recent jobs</CardTitle>
          <CardDescription>Last {jobs.length} upload jobs</CardDescription>
        </CardHeader>
        <CardContent>
          {jobs.length === 0 ? (
            <p className="text-sm text-muted-foreground">No jobs yet.</p>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>File</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead>Result</TableHead>
                  <TableHead className="text-right">Created</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {jobs.map((j) => (
                  <TableRow key={j.id}>
                    <TableCell className="font-medium">{j.fileName ?? "—"}</TableCell>
                    <TableCell>
                      <Badge variant={stateVariant(j.state)}>{j.state}</Badge>
                    </TableCell>
                    <TableCell className="max-w-[16rem] truncate">
                      {j.youTubeUrl ? (
                        <a className="text-blue-600 underline" href={j.youTubeUrl} target="_blank" rel="noreferrer">
                          {j.youTubeUrl}
                        </a>
                      ) : j.error ? (
                        <span className="text-destructive">{j.error}</span>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                    <TableCell className="text-right text-muted-foreground">
                      {new Date(j.createdAt).toLocaleString()}
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  );
}
