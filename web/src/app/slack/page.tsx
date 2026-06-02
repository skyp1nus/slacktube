import { requireSession, backendGet, backendBaseUrl } from "@/lib/backend";
import { SiteNav } from "@/components/site-nav";
import { buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { WorkspaceCard } from "./workspace-card";
import { OAuthToast } from "./oauth-toast";

export const dynamic = "force-dynamic";

type Workspace = {
  id: string;
  slackTeamId: string;
  teamName: string;
  botUserId: string | null;
  isActive: boolean;
  installedAt: string;
  channelCount: number;
};
type Channel = { id: string; slackChannelId: string; name: string; isPrivate: boolean; isMember: boolean };

export default async function SlackPage() {
  await requireSession();

  const workspaces = await backendGet<Workspace[]>("/api/admin/slack/workspaces").catch(() => [] as Workspace[]);
  const channelsByWs: Record<string, Channel[]> = {};
  await Promise.all(
    workspaces.map(async (w) => {
      channelsByWs[w.id] = await backendGet<Channel[]>(`/api/admin/slack/workspaces/${w.id}/channels`).catch(() => []);
    }),
  );

  return (
    <>
      <SiteNav />
      <OAuthToast />
      <div className="mx-auto w-full max-w-4xl space-y-6 p-6">
        <header className="flex flex-wrap items-start justify-between gap-4">
          <div>
            <h1 className="text-2xl font-semibold">Slack</h1>
            <p className="text-sm text-muted-foreground">
              Connect a workspace via OAuth, then invite the bot to the channels you want to watch.
            </p>
          </div>
          <a className={buttonVariants()} href={`${backendBaseUrl()}/slack/oauth/start`}>
            Connect Slack
          </a>
        </header>

        {workspaces.length === 0 ? (
          <Card>
            <CardContent className="py-12 text-center text-sm text-muted-foreground">
              No Slack workspaces connected yet. Click “Connect Slack” to install the bot.
            </CardContent>
          </Card>
        ) : (
          <div className="space-y-4">
            {workspaces.map((w) => (
              <WorkspaceCard key={w.id} workspace={w} channels={channelsByWs[w.id] ?? []} />
            ))}
          </div>
        )}
      </div>
    </>
  );
}
