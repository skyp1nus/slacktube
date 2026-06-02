import Link from "next/link";
import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import { SiteNav } from "@/components/site-nav";
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

export const dynamic = "force-dynamic";

type Status = {
  slackConfigured: boolean;
  workspaceCount: number;
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
      return "secondary";
  }
}

export default async function Dashboard() {
  await requireSession();

  const status = await backendGet<Status>("/api/admin/status").catch(() => null);
  const jobs = await backendGet<Job[]>("/api/admin/jobs").catch(() => [] as Job[]);
  const backendUnreachable = status === null;

  return (
    <>
      <SiteNav />
      <div className="mx-auto w-full max-w-4xl space-y-6 p-6">
        <header>
          <h1 className="text-2xl font-semibold">Dashboard</h1>
          <p className="text-sm text-muted-foreground">Slack → YouTube upload automation</p>
        </header>

        {backendUnreachable && (
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
                    <Badge>{status.workspaceCount} workspace{status.workspaceCount === 1 ? "" : "s"}</Badge>
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
                  {status?.google.connected ? <Badge>Connected</Badge> : <Badge variant="outline">Not connected</Badge>}
                </CardTitle>
              </CardHeader>
            </Card>
          </Link>
          <Card>
            <CardHeader className="pb-2">
              <CardDescription>Quota today (PT)</CardDescription>
              <CardTitle className="text-base">
                {status ? `${status.quota.remainingUploads}/${status.quota.totalUploads} uploads left` : "—"}
              </CardTitle>
            </CardHeader>
          </Card>
        </div>

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
    </>
  );
}
