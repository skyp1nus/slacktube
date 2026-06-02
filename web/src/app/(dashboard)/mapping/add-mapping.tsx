"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

type Channel = {
  id: string;
  slackChannelId: string;
  name: string;
  isPrivate: boolean;
  workspaceId: string;
  workspaceName: string;
};
type Account = { id: string; label: string; youTubeChannelTitle: string | null };

export function AddMapping({ channels, accounts }: { channels: Channel[]; accounts: Account[] }) {
  const router = useRouter();
  const [channelKey, setChannelKey] = useState<string | null>(null); // `${workspaceId}:${channelId}`
  const [accountId, setAccountId] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function create() {
    if (!channelKey || !accountId) {
      toast.error("Pick a channel and an account");
      return;
    }
    const [slackWorkspaceId, slackChannelId] = channelKey.split(":");
    setBusy(true);
    const res = await fetch("/api/admin/mappings", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ slackWorkspaceId, slackChannelId, googleAccountId: accountId }),
    });
    setBusy(false);
    if (res.ok) {
      toast.success("Mapping created");
      setChannelKey(null);
      setAccountId(null);
      router.refresh();
    } else {
      const j = await res.json().catch(() => ({}));
      toast.error(j.error === "already_mapped" ? "That channel is already mapped" : "Failed to create mapping");
    }
  }

  if (channels.length === 0 || accounts.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        Connect a Slack workspace (invite the bot to a channel) and a Google account first.
      </p>
    );
  }

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="min-w-[16rem] space-y-1">
        <span className="text-sm font-medium">Slack channel</span>
        <Select value={channelKey ?? undefined} onValueChange={setChannelKey}>
          <SelectTrigger className="w-full">
            <SelectValue placeholder="Pick a channel" />
          </SelectTrigger>
          <SelectContent>
            {channels.map((c) => (
              <SelectItem key={c.id} value={`${c.workspaceId}:${c.slackChannelId}`}>
                #{c.name} · {c.workspaceName}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="min-w-[16rem] space-y-1">
        <span className="text-sm font-medium">Google account</span>
        <Select value={accountId ?? undefined} onValueChange={setAccountId}>
          <SelectTrigger className="w-full">
            <SelectValue placeholder="Pick an account" />
          </SelectTrigger>
          <SelectContent>
            {accounts.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                {a.youTubeChannelTitle ?? a.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <Button disabled={busy} onClick={create}>
        {busy ? "…" : "Create mapping"}
      </Button>
    </div>
  );
}
