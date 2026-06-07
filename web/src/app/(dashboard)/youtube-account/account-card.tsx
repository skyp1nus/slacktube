"use client";

import { Boxes, CalendarDays, MonitorPlay } from "lucide-react";

import type { GoogleAccountDto } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Separator } from "@/components/ui/separator";
import { DisconnectAccountDialog } from "./disconnect-account-dialog";

const nf = new Intl.NumberFormat("en-US");
const dateFormatter = new Intl.DateTimeFormat("en-US", { dateStyle: "medium" });

function usagePercent(used: number, limit: number): number {
  if (limit <= 0) return 0;
  return Math.min(100, Math.max(0, Math.round((used / limit) * 100)));
}

export function AccountCard({ account }: { account: GoogleAccountDto }) {
  const { usedUploads, uploadLimit, remainingUploads, totalUploads, usedUnits, capUnits } = account.quota;
  // Upload capacity is gated by the videos.insert daily bucket, not the unit pool.
  const percent = usagePercent(usedUploads, uploadLimit);

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3 space-y-0">
        <div className="flex items-start gap-3">
          <Avatar className="size-9 shrink-0">
            {account.avatarUrl ? <AvatarImage src={account.avatarUrl} alt="" /> : null}
            <AvatarFallback className="bg-primary/10 text-primary">
              <MonitorPlay className="size-4.5" aria-hidden="true" />
            </AvatarFallback>
          </Avatar>
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <CardTitle className="text-base">{account.youTubeChannelTitle ?? account.label}</CardTitle>
              <Badge
                variant="outline"
                className={cn(
                  account.status === "Active" &&
                    "border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400",
                )}
              >
                {account.status}
              </Badge>
            </div>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span className="font-mono">{account.youTubeChannelId ?? "channel id unknown"}</span>
              {account.oAuthClientLabel && (
                <span className="inline-flex items-center gap-1.5">
                  <Boxes className="size-3.5" aria-hidden="true" />
                  {account.oAuthClientLabel}
                </span>
              )}
              <span className="inline-flex items-center gap-1.5">
                <CalendarDays className="size-3.5" aria-hidden="true" />
                Connected {dateFormatter.format(new Date(account.createdAt))}
              </span>
            </div>
          </div>
        </div>
        <DisconnectAccountDialog id={account.id} label={account.youTubeChannelTitle ?? account.label} />
      </CardHeader>

      <Separator />

      <CardContent className="space-y-1.5 py-3">
        <div className="flex items-center justify-between text-xs">
          <span className="text-muted-foreground">Project uploads today (PT, shared)</span>
          <span className={cn("font-medium tabular-nums", percent >= 90 && "text-destructive")}>{percent}%</span>
        </div>
        <Progress
          value={percent}
          className={cn(percent >= 90 && "[&>[data-slot=progress-indicator]]:bg-destructive")}
        />
        <div className="flex items-center justify-between text-xs text-muted-foreground tabular-nums">
          <span>
            {remainingUploads} of {totalUploads} uploads left today
          </span>
          <span title="Separate ~10k/day pool for non-upload API calls (list/search)">
            {nf.format(usedUnits)} / {nf.format(capUnits)} units
          </span>
        </div>
      </CardContent>
    </Card>
  );
}
