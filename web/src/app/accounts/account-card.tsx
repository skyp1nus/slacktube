"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";

type Account = {
  id: string;
  label: string;
  youTubeChannelId: string | null;
  youTubeChannelTitle: string | null;
  accountEmail: string | null;
  status: string;
  createdAt: string;
  quota: { usedUnits: number; remainingUploads: number; totalUploads: number };
};

export function AccountCard({ account }: { account: Account }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function disconnect() {
    if (!window.confirm(`Disconnect "${account.label}"? Mappings pointing to it will be removed.`)) return;
    setBusy(true);
    const res = await fetch(`/api/admin/accounts/${account.id}`, { method: "DELETE" });
    setBusy(false);
    if (res.ok) {
      toast.success("Account disconnected");
      router.refresh();
    } else if (res.status === 409) {
      toast.error("A mapping still points to this account — remove it first.");
    } else {
      toast.error("Failed to disconnect");
    }
  }

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0">
        <div>
          <CardTitle className="flex items-center gap-2 text-base">
            {account.youTubeChannelTitle ?? account.label}
            <Badge variant={account.status === "Active" ? "default" : "destructive"}>{account.status}</Badge>
          </CardTitle>
          <CardDescription>
            {account.youTubeChannelId ? `channel ${account.youTubeChannelId}` : "channel id unknown"}
            {" · "}
            {account.quota.remainingUploads}/{account.quota.totalUploads} uploads left today
          </CardDescription>
        </div>
        <Button variant="outline" size="sm" disabled={busy} onClick={disconnect}>
          Disconnect
        </Button>
      </CardHeader>
      <CardContent className="text-xs text-muted-foreground">
        Connected {new Date(account.createdAt).toLocaleString()}
      </CardContent>
    </Card>
  );
}
