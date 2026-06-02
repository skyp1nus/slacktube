"use client";

import { useState } from "react";
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  ScrollText,
} from "lucide-react";

import { JOB_STATES, type JobDto } from "@/lib/types";
import { useJobs } from "@/hooks/use-jobs";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const dateTimeFormatter = new Intl.DateTimeFormat("en-US", {
  month: "short",
  day: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "—" : dateTimeFormatter.format(date);
}

/** Sentinel for the "All" option (the Select forbids empty values). */
const ALL = "all";
const PAGE_SIZE_OPTIONS = [10, 25, 50] as const;
const DEFAULT_PAGE_SIZE = 25;

export default function JobsPage() {
  const [status, setStatus] = useState<string>(ALL);
  const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE);
  const [page, setPage] = useState(1);

  function handleStatusChange(value: string | null) {
    setStatus(value ?? ALL);
    setPage(1);
  }

  function handlePageSizeChange(value: string | null) {
    if (!value) return;
    setPageSize(Number(value));
    setPage(1);
  }

  const { data, isPending, isError, error, isFetching, refetch } = useJobs({
    status: status === ALL ? undefined : status,
    page,
    pageSize,
  });

  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const hasItems = (data?.items.length ?? 0) > 0;

  return (
    <div className="mx-auto w-full max-w-6xl space-y-6">
      <div>
        <h2 className="text-2xl font-semibold tracking-tight">Jobs</h2>
        <p className="text-sm text-muted-foreground">Upload queue + history.</p>
      </div>

      <div className="flex flex-wrap items-end gap-4">
        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Status</Label>
          <Select value={status} onValueChange={handleStatusChange}>
            <SelectTrigger size="sm" className="w-[160px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All statuses</SelectItem>
              {JOB_STATES.map((option) => (
                <SelectItem key={option} value={option}>
                  {option}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="ml-auto grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Per page</Label>
          <Select value={String(pageSize)} onValueChange={handlePageSizeChange}>
            <SelectTrigger size="sm" className="w-[100px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {PAGE_SIZE_OPTIONS.map((option) => (
                <SelectItem key={option} value={String(option)}>
                  {option}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      <Card>
        <CardContent className="px-0">
          {isPending ? (
            <TableSkeleton rows={Math.min(pageSize, 10)} />
          ) : isError ? (
            <ErrorState message={error instanceof Error ? error.message : "Failed to load jobs."} onRetry={() => refetch()} />
          ) : hasItems ? (
            <JobsTable items={data!.items} />
          ) : (
            <EmptyState filtered={status !== ALL} />
          )}
        </CardContent>
      </Card>

      {!isPending && !isError && total > 0 && (
        <Pagination
          page={page}
          totalPages={totalPages}
          total={total}
          isFetching={isFetching}
          onPageChange={setPage}
        />
      )}
    </div>
  );
}

function StateBadge({ state }: { state: string }) {
  if (state === "Done") {
    return (
      <Badge variant="outline" className="border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400">
        {state}
      </Badge>
    );
  }
  if (state === "Failed" || state === "Cancelled" || state === "Blocked") {
    return <Badge variant="destructive">{state}</Badge>;
  }
  if (state === "Queued") {
    return <Badge variant="outline">{state}</Badge>;
  }
  return (
    <Badge variant="outline" className="border-blue-500/30 bg-blue-500/10 text-blue-700 dark:text-blue-400">
      {state}
    </Badge>
  );
}

function JobsTable({ items }: { items: JobDto[] }) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="w-[150px] px-4">Time</TableHead>
          <TableHead className="w-[130px]">State</TableHead>
          <TableHead>File</TableHead>
          <TableHead>Result</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {items.map((job) => (
          <TableRow key={job.id}>
            <TableCell className="px-4 font-mono text-xs text-muted-foreground tabular-nums">
              {formatTimestamp(job.createdAt)}
            </TableCell>
            <TableCell>
              <StateBadge state={job.state} />
            </TableCell>
            <TableCell className="font-medium">{job.fileName ?? "—"}</TableCell>
            <TableCell className="max-w-[20rem] truncate">
              {job.youTubeUrl ? (
                <a className="text-primary underline" href={job.youTubeUrl} target="_blank" rel="noreferrer">
                  {job.youTubeUrl}
                </a>
              ) : job.error ? (
                <span className="text-destructive">{job.error}</span>
              ) : (
                <span className="text-muted-foreground">—</span>
              )}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function Pagination({
  page,
  totalPages,
  total,
  isFetching,
  onPageChange,
}: {
  page: number;
  totalPages: number;
  total: number;
  isFetching: boolean;
  onPageChange: (page: number) => void;
}) {
  const canPrev = page > 1;
  const canNext = page < totalPages;

  return (
    <div className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-sm text-muted-foreground">
        Page {page} of {totalPages}
        <span className="hidden sm:inline">
          {" · "}
          {total.toLocaleString("en-US")} {total === 1 ? "job" : "jobs"}
        </span>
        {isFetching && <span className="ml-2 opacity-70">Updating…</span>}
      </p>
      <div className="flex items-center gap-1.5">
        <Button variant="outline" size="icon-sm" disabled={!canPrev} onClick={() => onPageChange(1)} aria-label="First page">
          <ChevronsLeft aria-hidden={true} />
        </Button>
        <Button variant="outline" size="sm" disabled={!canPrev} onClick={() => onPageChange(page - 1)}>
          <ChevronLeft aria-hidden={true} />
          Prev
        </Button>
        <Button variant="outline" size="sm" disabled={!canNext} onClick={() => onPageChange(page + 1)}>
          Next
          <ChevronRight aria-hidden={true} />
        </Button>
        <Button variant="outline" size="icon-sm" disabled={!canNext} onClick={() => onPageChange(totalPages)} aria-label="Last page">
          <ChevronsRight aria-hidden={true} />
        </Button>
      </div>
    </div>
  );
}

function TableSkeleton({ rows }: { rows: number }) {
  return (
    <div className="space-y-3 p-4">
      {Array.from({ length: rows }).map((_, index) => (
        <div key={index} className="flex items-center gap-4">
          <Skeleton className="h-5 w-28" />
          <Skeleton className="h-5 w-20" />
          <Skeleton className="h-5 flex-1" />
          <Skeleton className="h-5 w-40" />
        </div>
      ))}
    </div>
  );
}

function EmptyState({ filtered }: { filtered: boolean }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-muted">
        <ScrollText className="size-6 text-muted-foreground" aria-hidden={true} />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">{filtered ? "No jobs match this filter" : "No jobs yet"}</p>
        <p className="text-sm text-muted-foreground">
          {filtered ? "Try clearing the status filter." : "Post an upload template in a mapped Slack channel."}
        </p>
      </div>
    </div>
  );
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-destructive/10">
        <AlertTriangle className="size-6 text-destructive" aria-hidden={true} />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">Couldn’t load jobs</p>
        <p className="text-sm text-muted-foreground">{message}</p>
      </div>
      <button
        type="button"
        onClick={onRetry}
        className="text-sm font-medium text-primary underline-offset-4 hover:underline"
      >
        Try again
      </button>
    </div>
  );
}
