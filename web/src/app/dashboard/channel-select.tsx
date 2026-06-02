"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";

type Channel = { id: string; name: string; isMember: boolean };

export function ChannelSelect({
  channels,
  current,
}: {
  channels: Channel[];
  current: string | null;
}) {
  const router = useRouter();
  const [saving, setSaving] = useState(false);

  async function onChange(channelId: string | null) {
    if (!channelId) return;
    setSaving(true);
    const res = await fetch("/api/admin/channel", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ channelId }),
    });
    setSaving(false);
    if (res.ok) {
      toast.success("Listening channel updated");
      router.refresh();
    } else {
      toast.error("Failed to update channel");
    }
  }

  if (channels.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        No channels available. Save Slack credentials first, then make sure the bot is invited to a channel.
      </p>
    );
  }

  return (
    <Select defaultValue={current ?? undefined} onValueChange={onChange} disabled={saving}>
      <SelectTrigger className="w-full">
        <SelectValue placeholder="Select a channel to listen to" />
      </SelectTrigger>
      <SelectContent>
        {channels.map((c) => (
          <SelectItem key={c.id} value={c.id}>
            #{c.name} {c.isMember ? "" : "(bot not a member)"}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
