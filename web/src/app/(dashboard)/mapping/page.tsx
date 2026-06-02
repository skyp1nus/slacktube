import { requireSession, backendGet } from "@/lib/backend";
import { Card, CardContent } from "@/components/ui/card";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { AddMapping } from "./add-mapping";
import { DeleteMappingButton } from "./delete-mapping-button";

export const dynamic = "force-dynamic";

type Mapping = {
  id: string;
  slackWorkspaceName: string;
  slackChannelName: string;
  googleAccountLabel: string;
  createdAt: string;
};
type Channel = {
  id: string;
  slackChannelId: string;
  name: string;
  isPrivate: boolean;
  workspaceId: string;
  workspaceName: string;
};
type Account = { id: string; label: string; youTubeChannelTitle: string | null };

export default async function MappingPage() {
  await requireSession();

  const [mappings, channels, accounts] = await Promise.all([
    backendGet<Mapping[]>("/api/admin/mappings").catch(() => [] as Mapping[]),
    backendGet<Channel[]>("/api/admin/slack/channels").catch(() => [] as Channel[]),
    backendGet<Account[]>("/api/admin/google/accounts").catch(() => [] as Account[]),
  ]);

  return (
    <>
      <div className="mx-auto w-full max-w-5xl space-y-6">
        <header>
          <h2 className="text-2xl font-semibold tracking-tight">Mapping</h2>
          <p className="text-sm text-muted-foreground">
            Route a Slack channel to a Google account. Uploads posted in a channel go to its account.
          </p>
        </header>

        <Card>
          <CardContent className="p-4">
            <AddMapping channels={channels} accounts={accounts} />
          </CardContent>
        </Card>

        <Card>
          <CardContent className="p-0">
            {mappings.length === 0 ? (
              <p className="p-6 text-sm text-muted-foreground">No mappings yet.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Slack channel</TableHead>
                    <TableHead>Workspace</TableHead>
                    <TableHead>Google account</TableHead>
                    <TableHead className="text-right">Actions</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {mappings.map((m) => (
                    <TableRow key={m.id}>
                      <TableCell className="font-medium">#{m.slackChannelName}</TableCell>
                      <TableCell className="text-muted-foreground">{m.slackWorkspaceName}</TableCell>
                      <TableCell>{m.googleAccountLabel}</TableCell>
                      <TableCell className="text-right">
                        <DeleteMappingButton id={m.id} />
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
