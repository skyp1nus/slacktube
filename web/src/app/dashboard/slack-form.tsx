"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";

export function SlackForm({ configured }: { configured: boolean }) {
  const router = useRouter();
  const [botToken, setBotToken] = useState("");
  const [signingSecret, setSigningSecret] = useState("");
  const [loading, setLoading] = useState(false);

  async function save(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    const res = await fetch("/api/admin/slack", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ botToken, signingSecret }),
    });
    setLoading(false);
    if (res.ok) {
      toast.success("Slack credentials saved");
      setBotToken("");
      setSigningSecret("");
      router.refresh();
    } else {
      toast.error("Failed to save Slack credentials");
    }
  }

  return (
    <form onSubmit={save} className="space-y-3">
      <div className="space-y-2">
        <Label htmlFor="botToken">Bot token (xoxb-…)</Label>
        <Input
          id="botToken"
          value={botToken}
          onChange={(e) => setBotToken(e.target.value)}
          placeholder={configured ? "•••••••• (set — re-enter to replace)" : "xoxb-…"}
          required
        />
      </div>
      <div className="space-y-2">
        <Label htmlFor="signingSecret">Signing secret</Label>
        <Input
          id="signingSecret"
          value={signingSecret}
          onChange={(e) => setSigningSecret(e.target.value)}
          placeholder={configured ? "•••••••• (set — re-enter to replace)" : "signing secret"}
          required
        />
      </div>
      <Button type="submit" disabled={loading}>
        {loading ? "Saving…" : "Save Slack credentials"}
      </Button>
    </form>
  );
}
