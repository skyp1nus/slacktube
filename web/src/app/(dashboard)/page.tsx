"use client";

import type { ComponentType } from "react";
import { AlertTriangle, ExternalLink, Gauge, Hash, Inbox, MonitorPlay, Plug, UploadCloud } from "lucide-react";

import type { DashboardStats, JobDto } from "@/lib/types";
import { cn } from "@/lib/utils";
import { useDashboardStats } from "@/hooks/use-dashboard";
import { useRecentJobs } from "@/hooks/use-jobs";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";

const nf = new Intl.NumberFormat("en-US");

function clampPercent(used: number, cap: number): number {
  if (cap <= 0) return 0;
  return Math.min(100, Math.max(0, Math.round((used / cap) * 100)));
}

export default function DashboardPage() {
  const { data: stats, isPending, isError, error, refetch } = useDashboardStats();

  return (
    <div className="mx-auto w-full max-w-6xl space-y-6">
      <div>
        <h2 className="text-2xl font-semibold tracking-tight">Dashboard</h2>
        <p className="text-sm text-muted-foreground">Overview of your Slack → YouTube upload activity.</p>
      </div>

      {isError ? (
        <ErrorState message={error instanceof Error ? error.message : "Failed to load dashboard."} onRetry={() => refetch()} />
      ) : (
        <>
          <KpiCards stats={stats} isLoading={isPending} />
          <SecondaryStats stats={stats} isLoading={isPending} />
          <RecentJobsCard />
        </>
      )}
    </div>
  );
}

function KpiCards({ stats, isLoading }: { stats: DashboardStats | undefined; isLoading: boolean }) {
  return (
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <KpiCard label="Workspaces" icon={Plug} isLoading={isLoading} value={stats ? nf.format(stats.workspaceCount) : "—"} hint="Connected Slack" />
      <KpiCard
        label="Google accounts"
        icon={MonitorPlay}
        isLoading={isLoading}
        value={stats ? nf.format(stats.accountCount) : "—"}
        hint="YouTube channels"
      />
      <KpiCard
        label="Uploads today"
        icon={UploadCloud}
        isLoading={isLoading}
        value={stats ? nf.format(stats.uploadsToday) : "—"}
        hint={stats ? `${nf.format(stats.uploadsLast24h)} in last 24h` : undefined}
      />
      <QuotaCard stats={stats} isLoading={isLoading} />
    </div>
  );
}

function KpiCard({
  label,
  icon: Icon,
  value,
  hint,
  valueClassName,
  isLoading,
}: {
  label: string;
  icon: ComponentType<{ className?: string; "aria-hidden"?: boolean }>;
  value: string;
  hint?: string;
  valueClassName?: string;
  isLoading: boolean;
}) {
  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
        <CardTitle className="text-sm font-medium text-muted-foreground">{label}</CardTitle>
        <Icon className="size-4 text-muted-foreground" aria-hidden={true} />
      </CardHeader>
      <CardContent className="space-y-1">
        {isLoading ? (
          <Skeleton className="h-8 w-20" />
        ) : (
          <span className={cn("text-2xl font-semibold tracking-tight", valueClassName)}>{value}</span>
        )}
        {isLoading ? <Skeleton className="h-3.5 w-24" /> : hint ? <p className="text-xs text-muted-foreground">{hint}</p> : null}
      </CardContent>
    </Card>
  );
}

function QuotaCard({ stats, isLoading }: { stats: DashboardStats | undefined; isLoading: boolean }) {
  const percent = stats ? clampPercent(stats.quotaUsedUnits, stats.quotaCapUnits) : 0;

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
        <CardTitle className="text-sm font-medium text-muted-foreground">Quota used (PT)</CardTitle>
        <Gauge className="size-4 text-muted-foreground" aria-hidden={true} />
      </CardHeader>
      <CardContent className="space-y-2">
        {isLoading || !stats ? (
          <>
            <Skeleton className="h-8 w-16" />
            <Skeleton className="h-1 w-full" />
            <Skeleton className="h-3.5 w-28" />
          </>
        ) : (
          <>
            <span className={cn("text-2xl font-semibold tracking-tight", percent >= 90 && "text-destructive")}>{percent}%</span>
            <Progress value={percent} className={cn(percent >= 90 && "[&>[data-slot=progress-indicator]]:bg-destructive")} />
            <p className="text-xs text-muted-foreground tabular-nums">
              {nf.format(stats.quotaUsedUnits)} / {nf.format(stats.quotaCapUnits)} units
            </p>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function SecondaryStats({ stats, isLoading }: { stats: DashboardStats | undefined; isLoading: boolean }) {
  const items = [
    { label: "Errors (24h)", value: stats?.errorsLast24h ?? 0, icon: AlertTriangle, danger: (stats?.errorsLast24h ?? 0) > 0 },
    { label: "Uploads (24h)", value: stats?.uploadsLast24h ?? 0, icon: UploadCloud, danger: false },
  ];

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      {items.map((item) => {
        const Icon = item.icon;
        return (
          <Card key={item.label} size="sm">
            <CardContent className="flex items-center gap-3">
              <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-muted">
                <Icon className="size-4 text-muted-foreground" aria-hidden={true} />
              </div>
              <div className="min-w-0">
                {isLoading ? (
                  <Skeleton className="h-6 w-10" />
                ) : (
                  <p className={cn("text-lg font-semibold leading-tight tracking-tight", item.danger && "text-destructive")}>
                    {nf.format(item.value)}
                  </p>
                )}
                <p className="truncate text-xs text-muted-foreground">{item.label}</p>
              </div>
            </CardContent>
          </Card>
        );
      })}
    </div>
  );
}

const rtf = new Intl.RelativeTimeFormat("en-US", { numeric: "auto", style: "narrow" });

/** Compact "2h ago" style timestamp for the recent-jobs list. */
function relativeTime(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "—";
  const diffSec = Math.round((then - Date.now()) / 1000);
  const abs = Math.abs(diffSec);
  if (abs < 60) return rtf.format(diffSec, "second");
  if (abs < 3600) return rtf.format(Math.round(diffSec / 60), "minute");
  if (abs < 86_400) return rtf.format(Math.round(diffSec / 3600), "hour");
  return rtf.format(Math.round(diffSec / 86_400), "day");
}

function RecentStateBadge({ state }: { state: string }) {
  if (state === "Done") {
    return (
      <Badge variant="outline" className="shrink-0 border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400">
        {state}
      </Badge>
    );
  }
  if (state === "Failed" || state === "Cancelled" || state === "Blocked") {
    return (
      <Badge variant="destructive" className="shrink-0">
        {state}
      </Badge>
    );
  }
  if (state === "Queued") {
    return (
      <Badge variant="outline" className="shrink-0">
        {state}
      </Badge>
    );
  }
  return (
    <Badge variant="outline" className="shrink-0 border-blue-500/30 bg-blue-500/10 text-blue-700 dark:text-blue-400">
      {state}
    </Badge>
  );
}

function RecentJobsCard() {
  const { data, isPending } = useRecentJobs();
  const jobs: JobDto[] = data?.items ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Recent jobs</CardTitle>
        <CardDescription>Last 24 hours{data ? ` · ${nf.format(data.total)} total` : ""}</CardDescription>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <div className="space-y-0.5">
            {Array.from({ length: 4 }).map((_, i) => (
              <div key={i} className="flex items-center gap-3 px-2 py-2.5">
                <Skeleton className="h-5 w-16 rounded-full" />
                <div className="flex-1 space-y-1.5">
                  <Skeleton className="h-4 w-2/3" />
                  <Skeleton className="h-3 w-1/3" />
                </div>
                <Skeleton className="h-4 w-12" />
              </div>
            ))}
          </div>
        ) : jobs.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-2 py-10 text-center">
            <div className="flex size-10 items-center justify-center rounded-full bg-muted">
              <Inbox className="size-5 text-muted-foreground" aria-hidden={true} />
            </div>
            <p className="text-sm text-muted-foreground">No jobs in the last 24 hours.</p>
          </div>
        ) : (
          <ul className="-mx-2 space-y-0.5">
            {jobs.map((j) => {
              const channel = j.channelName ?? j.slackChannelId;
              return (
                <li
                  key={j.id}
                  className="flex items-center gap-3 rounded-lg px-2 py-2.5 transition-colors hover:bg-muted/50"
                >
                  <RecentStateBadge state={j.state} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium leading-tight">{j.fileName ?? "Untitled"}</p>
                    <div className="mt-0.5 flex items-center gap-1.5 text-xs text-muted-foreground">
                      {channel ? (
                        <span className="flex min-w-0 items-center gap-0.5">
                          <Hash className="size-3 shrink-0" aria-hidden={true} />
                          <span className="truncate">{channel}</span>
                        </span>
                      ) : null}
                      {channel ? <span aria-hidden={true}>·</span> : null}
                      <span className="shrink-0 tabular-nums">{relativeTime(j.createdAt)}</span>
                    </div>
                  </div>
                  {j.youTubeUrl ? (
                    <a
                      className="flex shrink-0 items-center gap-1 text-sm text-primary hover:underline"
                      href={j.youTubeUrl}
                      target="_blank"
                      rel="noreferrer"
                    >
                      <ExternalLink className="size-3.5" aria-hidden={true} />
                      <span className="hidden sm:inline">Watch</span>
                    </a>
                  ) : j.error ? (
                    <span className="max-w-[12rem] shrink-0 truncate text-xs text-destructive" title={j.error}>
                      {j.error}
                    </span>
                  ) : null}
                </li>
              );
            })}
          </ul>
        )}
      </CardContent>
    </Card>
  );
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
        <div className="flex size-12 items-center justify-center rounded-full bg-destructive/10">
          <AlertTriangle className="size-6 text-destructive" aria-hidden={true} />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">Couldn’t load dashboard</p>
          <p className="text-sm text-muted-foreground">{message}</p>
        </div>
        <button
          type="button"
          onClick={onRetry}
          className="text-sm font-medium text-primary underline-offset-4 hover:underline"
        >
          Try again
        </button>
      </CardContent>
    </Card>
  );
}
