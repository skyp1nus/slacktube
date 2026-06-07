"use client";

import type { ComponentType } from "react";
import { AlertTriangle, HardDriveDownload, Hash, MonitorPlay, type LucideIcon } from "lucide-react";

import { useApiUsage } from "@/hooks/use-usage";
import type { UsageGroup, UsageMetric } from "@/lib/types";
import { cn } from "@/lib/utils";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { Skeleton } from "@/components/ui/skeleton";

const nf = new Intl.NumberFormat("en-US");

const groupIcon: Record<string, LucideIcon> = {
  YouTube: MonitorPlay,
  Drive: HardDriveDownload,
  Slack: Hash,
};

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = bytes / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i += 1;
  }
  return `${v.toFixed(v >= 100 ? 0 : 1)} ${units[i]}`;
}

/** "1.0 MB" for byte metrics, otherwise "1,234 queries". */
function formatValue(unit: string, n: number): string {
  return unit === "bytes" ? formatBytes(n) : `${nf.format(n)} ${unit}`;
}

function formatLimit(unit: string, limit: number): string {
  return unit === "bytes" ? formatBytes(limit) : nf.format(limit);
}

function percent(used: number, limit: number | null): number | null {
  if (limit === null || limit <= 0) return null;
  return Math.min(100, Math.max(0, Math.round((used / limit) * 100)));
}

export default function UsagePage() {
  const { data, isPending, isError, error, refetch } = useApiUsage();

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <div>
        <h2 className="text-2xl font-semibold tracking-tight">API Usage</h2>
        <p className="text-sm text-muted-foreground">
          Daily spend across the YouTube, Drive, and Slack APIs — resets at midnight Pacific Time
          {data ? ` · ${data.date} (PT)` : ""}.
        </p>
      </div>

      {isError ? (
        <ErrorState
          message={error instanceof Error ? error.message : "Failed to load usage."}
          onRetry={() => refetch()}
        />
      ) : isPending || !data ? (
        <LoadingGroups />
      ) : (
        data.groups.map((group) => <UsageGroupCard key={group.group} group={group} />)
      )}
    </div>
  );
}

function UsageGroupCard({ group }: { group: UsageGroup }) {
  const Icon: ComponentType<{ className?: string; "aria-hidden"?: boolean }> = groupIcon[group.group] ?? HardDriveDownload;

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between gap-2 space-y-0">
        <CardTitle className="text-base">{group.group}</CardTitle>
        <Icon className="size-4 text-muted-foreground" aria-hidden={true} />
      </CardHeader>
      <CardContent className="space-y-4">
        {group.metrics.length === 0 ? (
          <p className="text-sm text-muted-foreground">No calls yet today.</p>
        ) : (
          group.metrics.map((metric) => <MetricRow key={metric.key} metric={metric} />)
        )}
      </CardContent>
    </Card>
  );
}

function MetricRow({ metric }: { metric: UsageMetric }) {
  const pct = percent(metric.used, metric.limit);
  const danger = pct !== null && pct >= 90;

  return (
    <div className="space-y-1.5">
      <div className="flex items-center justify-between gap-3 text-sm">
        <span className="text-muted-foreground">{metric.label}</span>
        <span className={cn("font-medium tabular-nums", danger && "text-destructive")}>
          {metric.limit !== null
            ? `${formatValue(metric.unit, metric.used)} / ${formatLimit(metric.unit, metric.limit)}`
            : formatValue(metric.unit, metric.used)}
        </span>
      </div>
      {pct !== null && (
        <Progress value={pct} className={cn(danger && "[&>[data-slot=progress-indicator]]:bg-destructive")} />
      )}
      {metric.perScope.length > 1 && (
        <div className="flex flex-wrap gap-x-4 gap-y-0.5 pt-0.5 text-xs text-muted-foreground tabular-nums">
          {metric.perScope.map((s, i) => (
            <span key={`${s.scope}-${i}`}>
              {s.scope}: {formatValue(metric.unit, s.used)}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}

function LoadingGroups() {
  return (
    <>
      {Array.from({ length: 3 }).map((_, i) => (
        <Card key={i}>
          <CardHeader>
            <Skeleton className="h-5 w-24" />
          </CardHeader>
          <CardContent className="space-y-4">
            {Array.from({ length: 2 }).map((_, j) => (
              <div key={j} className="space-y-1.5">
                <div className="flex justify-between">
                  <Skeleton className="h-4 w-40" />
                  <Skeleton className="h-4 w-24" />
                </div>
                <Skeleton className="h-2 w-full" />
              </div>
            ))}
          </CardContent>
        </Card>
      ))}
    </>
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
          <p className="text-sm font-medium">Couldn’t load usage</p>
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
