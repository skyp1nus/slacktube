"use client";

import { useEffect, useState } from "react";
import {
  AlertTriangle,
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  ScrollText,
  Search,
  X,
} from "lucide-react";

import { JOB_STATES, type JobDto } from "@/lib/types";
import { useJobFilters, useJobs } from "@/hooks/use-jobs";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
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
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";

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

/** Humanize a duration between two ISO timestamps (e.g. "2m 13s"); "—" for non-positive/invalid. */
function formatDuration(startIso: string, endIso: string): string {
  const start = new Date(startIso).getTime();
  const end = new Date(endIso).getTime();
  if (Number.isNaN(start) || Number.isNaN(end)) return "—";
  const ms = end - start;
  if (ms <= 0) return "—";
  const totalSeconds = Math.floor(ms / 1000);
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (hours > 0) return `${hours}h ${minutes}m`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

/** Sentinel for the "All" option (the Select forbids empty values). */
const ALL = "all";
const PAGE_SIZE_OPTIONS = [10, 25, 50] as const;
const DEFAULT_PAGE_SIZE = 25;
const SEARCH_DEBOUNCE_MS = 300;
const MAX_VISIBLE_TAGS = 3;

export default function JobsPage() {
  const [status, setStatus] = useState<string>(ALL);
  const [channel, setChannel] = useState<string>(ALL);
  const [tag, setTag] = useState<string>(ALL);
  const [account, setAccount] = useState<string>(ALL);
  const [fromDate, setFromDate] = useState<string>("");
  const [toDate, setToDate] = useState<string>("");
  const [searchInput, setSearchInput] = useState<string>("");
  const [search, setSearch] = useState<string>("");
  const [pageSize, setPageSize] = useState<number>(DEFAULT_PAGE_SIZE);
  const [page, setPage] = useState(1);

  // Debounce the search box so typing doesn't spam the API.
  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchInput);
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchInput]);

  const filters = useJobFilters();

  function handleStatusChange(value: string | null) {
    setStatus(value ?? ALL);
    setPage(1);
  }

  function handleChannelChange(value: string | null) {
    setChannel(value ?? ALL);
    setPage(1);
  }

  function handleTagChange(value: string | null) {
    setTag(value ?? ALL);
    setPage(1);
  }

  function handleAccountChange(value: string | null) {
    setAccount(value ?? ALL);
    setPage(1);
  }

  function handleFromChange(value: string) {
    setFromDate(value);
    setPage(1);
  }

  function handleToChange(value: string) {
    setToDate(value);
    setPage(1);
  }

  function handlePageSizeChange(value: string | null) {
    if (!value) return;
    setPageSize(Number(value));
    setPage(1);
  }

  function clearFilters() {
    setStatus(ALL);
    setChannel(ALL);
    setTag(ALL);
    setAccount(ALL);
    setFromDate("");
    setToDate("");
    setSearchInput("");
    setSearch("");
    setPage(1);
  }

  const hasActiveFilters =
    status !== ALL ||
    channel !== ALL ||
    tag !== ALL ||
    account !== ALL ||
    fromDate !== "" ||
    toDate !== "" ||
    search !== "";

  const { data, isPending, isError, error, isFetching, refetch } = useJobs({
    status: status === ALL ? undefined : status,
    channel: channel === ALL ? undefined : channel,
    tag: tag === ALL ? undefined : tag,
    account: account === ALL ? undefined : account,
    from: fromDate ? `${fromDate}T00:00:00.000Z` : undefined,
    to: toDate ? `${toDate}T23:59:59.999Z` : undefined,
    search: search || undefined,
    page,
    pageSize,
  });

  const total = data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const hasItems = (data?.items.length ?? 0) > 0;

  const channelOptions = filters.data?.channels ?? [];
  const tagOptions = filters.data?.tags ?? [];
  const accountOptions = filters.data?.accounts ?? [];

  return (
    <div className="mx-auto w-full max-w-6xl space-y-6">
      <div>
        <h2 className="text-2xl font-semibold tracking-tight">Jobs</h2>
        <p className="text-sm text-muted-foreground">Upload queue + history.</p>
      </div>

      <div className="flex flex-wrap items-end gap-3">
        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Status</Label>
          <Select value={status} onValueChange={handleStatusChange}>
            <SelectTrigger size="sm" className="w-[150px]">
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

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Channel</Label>
          <Select value={channel} onValueChange={handleChannelChange}>
            <SelectTrigger size="sm" className="w-[170px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All channels</SelectItem>
              {channelOptions.map((option) => (
                <SelectItem key={option.id} value={option.id}>
                  {option.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Tag</Label>
          <Select value={tag} onValueChange={handleTagChange}>
            <SelectTrigger size="sm" className="w-[150px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All tags</SelectItem>
              {tagOptions.map((option) => (
                <SelectItem key={option} value={option}>
                  {option}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Account</Label>
          <Select value={account} onValueChange={handleAccountChange}>
            <SelectTrigger size="sm" className="w-[170px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All accounts</SelectItem>
              {accountOptions.map((option) => (
                <SelectItem key={option.id} value={option.id}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">From</Label>
          <Input
            type="date"
            value={fromDate}
            max={toDate || undefined}
            onChange={(e) => handleFromChange(e.target.value)}
            className="w-[150px]"
          />
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">To</Label>
          <Input
            type="date"
            value={toDate}
            min={fromDate || undefined}
            onChange={(e) => handleToChange(e.target.value)}
            className="w-[150px]"
          />
        </div>

        <div className="grid gap-1.5">
          <Label className="text-xs text-muted-foreground">Search</Label>
          <div className="relative">
            <Search
              className="pointer-events-none absolute left-2.5 top-1/2 size-3.5 -translate-y-1/2 text-muted-foreground"
              aria-hidden={true}
            />
            <Input
              type="search"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="Search file name"
              className="w-[200px] pl-8"
            />
          </div>
        </div>

        {hasActiveFilters && (
          <Button variant="ghost" size="sm" onClick={clearFilters}>
            <X aria-hidden={true} />
            Clear filters
          </Button>
        )}

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
            <EmptyState filtered={hasActiveFilters} onClear={clearFilters} />
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

function TagCell({ tags }: { tags: string[] }) {
  if (!tags || tags.length === 0) {
    return <span className="text-muted-foreground">—</span>;
  }
  const visible = tags.slice(0, MAX_VISIBLE_TAGS);
  const overflow = tags.length - visible.length;
  return (
    <div className="flex flex-wrap items-center gap-1">
      {visible.map((t) => (
        <Badge key={t} variant="secondary" className="max-w-[8rem] truncate font-normal">
          {t}
        </Badge>
      ))}
      {overflow > 0 && (
        <Tooltip>
          <TooltipTrigger asChild>
            <Badge variant="outline" className="cursor-default font-normal">
              +{overflow}
            </Badge>
          </TooltipTrigger>
          <TooltipContent>{tags.slice(MAX_VISIBLE_TAGS).join(", ")}</TooltipContent>
        </Tooltip>
      )}
    </div>
  );
}

function JobsTable({ items }: { items: JobDto[] }) {
  return (
    <Table className="min-w-[1100px]">
      <TableHeader>
        <TableRow>
          <TableHead className="w-[130px] px-4">Time</TableHead>
          <TableHead className="w-[120px]">State</TableHead>
          <TableHead className="w-[150px]">Channel</TableHead>
          <TableHead>File</TableHead>
          <TableHead className="w-[200px]">Tags</TableHead>
          <TableHead className="w-[150px]">Account</TableHead>
          <TableHead className="w-[200px]">Result</TableHead>
          <TableHead className="w-[130px]">Updated</TableHead>
          <TableHead className="w-[100px] px-4">Duration</TableHead>
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
            <TableCell className="max-w-[150px] truncate">
              {job.channelName ?? job.slackChannelId ?? "—"}
            </TableCell>
            <TableCell className="max-w-[18rem] truncate font-medium">{job.fileName ?? "—"}</TableCell>
            <TableCell>
              <TagCell tags={job.tags} />
            </TableCell>
            <TableCell className="max-w-[150px] truncate">
              {job.googleAccountLabel ?? <span className="text-muted-foreground">—</span>}
            </TableCell>
            <TableCell className="max-w-[200px] truncate">
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
            <TableCell className="font-mono text-xs text-muted-foreground tabular-nums">
              {formatTimestamp(job.updatedAt)}
            </TableCell>
            <TableCell className="px-4 font-mono text-xs text-muted-foreground tabular-nums">
              {formatDuration(job.createdAt, job.updatedAt)}
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
          <Skeleton className="h-5 w-24" />
          <Skeleton className="h-5 w-20" />
          <Skeleton className="h-5 w-28" />
          <Skeleton className="h-5 flex-1" />
          <Skeleton className="h-5 w-32" />
          <Skeleton className="h-5 w-28" />
          <Skeleton className="h-5 w-36" />
          <Skeleton className="h-5 w-24" />
          <Skeleton className="h-5 w-16" />
        </div>
      ))}
    </div>
  );
}

function EmptyState({ filtered, onClear }: { filtered: boolean; onClear: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-muted">
        <ScrollText className="size-6 text-muted-foreground" aria-hidden={true} />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">{filtered ? "No jobs match these filters" : "No jobs yet"}</p>
        <p className="text-sm text-muted-foreground">
          {filtered ? "Try adjusting or clearing the filters." : "Post an upload template in a mapped Slack channel."}
        </p>
      </div>
      {filtered && (
        <Button variant="outline" size="sm" onClick={onClear}>
          <X aria-hidden={true} />
          Clear filters
        </Button>
      )}
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
