"use client";

import type { ComponentType } from "react";
import { AlertTriangle, Gauge, MonitorPlay, Plug, UploadCloud } from "lucide-react";

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

function RecentJobsCard() {
  const { data, isPending } = useRecentJobs();
  const jobs: JobDto[] = data?.items ?? [];

  return (
    <Card>
      <CardHeader>
        <CardTitle>Recent jobs</CardTitle>
        <CardDescription>Latest uploads{data ? ` · ${data.total} total` : ""}</CardDescription>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <div className="space-y-2">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-5 w-full" />
            ))}
          </div>
        ) : jobs.length === 0 ? (
          <p className="text-sm text-muted-foreground">No jobs yet.</p>
        ) : (
          <ul className="divide-y">
            {jobs.map((j) => (
              <li key={j.id} className="flex items-center gap-3 py-2 text-sm">
                <Badge variant={stateVariant(j.state)}>{j.state}</Badge>
                <span className="min-w-0 flex-1 truncate font-medium">{j.fileName ?? "—"}</span>
                {j.youTubeUrl ? (
                  <a className="shrink-0 text-primary underline" href={j.youTubeUrl} target="_blank" rel="noreferrer">
                    link
                  </a>
                ) : j.error ? (
                  <span className="max-w-[14rem] shrink-0 truncate text-destructive">{j.error}</span>
                ) : null}
              </li>
            ))}
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
