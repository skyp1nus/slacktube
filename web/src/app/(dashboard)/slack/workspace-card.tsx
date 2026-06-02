"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

type Channel = { id: string; slackChannelId: string; name: string; isPrivate: boolean; isMember: boolean };
type Workspace = {
  id: string;
  teamName: string;
  botUserId: string | null;
  isActive: boolean;
  installedAt: string;
  channelCount: number;
};

export function WorkspaceCard({ workspace, channels }: { workspace: Workspace; channels: Channel[] }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function refresh() {
    setBusy(true);
    const res = await fetch(`/api/admin/slack/workspaces/${workspace.id}/refresh-channels`, { method: "POST" });
    setBusy(false);
    if (res.ok) {
      toast.success("Channels refreshed");
      router.refresh();
    } else {
      toast.error("Failed to refresh channels");
    }
  }

  async function disconnect() {
    if (!window.confirm(`Disconnect "${workspace.teamName}"? This removes its channels and any mappings.`)) return;
    setBusy(true);
    const res = await fetch(`/api/admin/slack/workspaces/${workspace.id}`, { method: "DELETE" });
    setBusy(false);
    if (res.ok) {
      toast.success("Workspace disconnected");
      router.refresh();
    } else {
      toast.error("Failed to disconnect");
    }
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            {workspace.teamName}
            {workspace.isActive ? <Badge>Active</Badge> : <Badge variant="outline">Inactive</Badge>}
          </CardTitle>
          <CardDescription>
            {workspace.channelCount} channel{workspace.channelCount === 1 ? "" : "s"} ·{" "}
            installed {new Date(workspace.installedAt).toLocaleDateString()}
          </CardDescription>
        </div>
        <div className="flex gap-2">
          <Button variant="outline" size="sm" disabled={busy} onClick={refresh}>
            {busy ? "…" : "Refresh channels"}
          </Button>
          <Button variant="outline" size="sm" disabled={busy} onClick={disconnect}>
            Disconnect
          </Button>
        </div>
      </CardHeader>
      <CardContent>
        {channels.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No channels yet. Invite the bot to a channel, then “Refresh channels”.
          </p>
        ) : (
          <ul className="grid gap-1.5 text-sm sm:grid-cols-2">
            {channels.map((c) => (
              <li key={c.id} className="flex items-center gap-1.5">
                <span className="text-muted-foreground">{c.isPrivate ? "🔒" : "#"}</span>
                <span className="truncate">{c.name}</span>
                {!c.isMember && (
                  <span className="text-xs text-amber-600" title="Bot is not a member">
                    (bot not invited)
                  </span>
                )}
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}
