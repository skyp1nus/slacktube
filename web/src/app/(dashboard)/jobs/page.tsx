import { ScrollText } from "lucide-react";
import { requireSession, backendGet } from "@/lib/backend";
import { Card, CardContent } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export const dynamic = "force-dynamic";

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

export default async function JobsPage() {
  await requireSession();
  const jobs = await backendGet<Job[]>("/api/admin/jobs").catch(() => [] as Job[]);

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <header>
        <h2 className="text-2xl font-semibold tracking-tight">Jobs</h2>
        <p className="text-sm text-muted-foreground">Upload queue + recent history (last {jobs.length}).</p>
      </header>

      <Card>
        <CardContent className="p-0">
          {jobs.length === 0 ? (
            <div className="flex flex-col items-center justify-center gap-3 py-16 text-center">
              <div className="flex size-12 items-center justify-center rounded-full bg-muted">
                <ScrollText className="size-6 text-muted-foreground" aria-hidden="true" />
              </div>
              <p className="text-sm font-medium">No jobs yet</p>
              <p className="text-sm text-muted-foreground">
                Post an upload template in a mapped Slack channel.
              </p>
            </div>
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead className="px-4">File</TableHead>
                  <TableHead>State</TableHead>
                  <TableHead>Result</TableHead>
                  <TableHead className="px-4 text-right">Created</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {jobs.map((j) => (
                  <TableRow key={j.id}>
                    <TableCell className="px-4 font-medium">{j.fileName ?? "—"}</TableCell>
                    <TableCell>
                      <Badge variant={stateVariant(j.state)}>{j.state}</Badge>
                    </TableCell>
                    <TableCell className="max-w-[18rem] truncate">
                      {j.youTubeUrl ? (
                        <a className="text-primary underline" href={j.youTubeUrl} target="_blank" rel="noreferrer">
                          {j.youTubeUrl}
                        </a>
                      ) : j.error ? (
                        <span className="text-destructive">{j.error}</span>
                      ) : (
                        "—"
                      )}
                    </TableCell>
                    <TableCell className="px-4 text-right text-muted-foreground">
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
