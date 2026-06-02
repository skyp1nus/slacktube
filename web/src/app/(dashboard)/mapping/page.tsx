"use client";

import { AlertTriangle, Link2 } from "lucide-react";

import type { ChannelMappingDto } from "@/lib/types";
import { useMappings } from "@/hooks/use-mappings";
import { AddMappingDialog } from "./add-mapping-dialog";
import { DeleteMappingDialog } from "./delete-mapping-dialog";
import { Card, CardContent } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

export default function MappingPage() {
  const { data: mappings, isPending, isError, error, refetch } = useMappings();

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-semibold tracking-tight">Mapping</h2>
          <p className="text-sm text-muted-foreground">
            Route a Slack channel to a Google account. Uploads posted in a channel go to its account.
          </p>
        </div>
        <AddMappingDialog />
      </div>

      <Card>
        <CardContent className="px-0">
          {isPending ? (
            <TableSkeleton />
          ) : isError ? (
            <ErrorState
              message={error instanceof Error ? error.message : "Failed to load mappings."}
              onRetry={() => refetch()}
            />
          ) : mappings && mappings.length > 0 ? (
            <MappingsTable mappings={mappings} />
          ) : (
            <EmptyState />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

function MappingsTable({ mappings }: { mappings: ChannelMappingDto[] }) {
  return (
    <Table>
      <TableHeader>
        <TableRow>
          <TableHead className="px-4">Slack channel</TableHead>
          <TableHead>Workspace</TableHead>
          <TableHead>Google account</TableHead>
          <TableHead className="px-4 text-right">Actions</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {mappings.map((mapping) => (
          <TableRow key={mapping.id}>
            <TableCell className="px-4 font-medium">#{mapping.slackChannelName}</TableCell>
            <TableCell className="text-muted-foreground">{mapping.slackWorkspaceName}</TableCell>
            <TableCell>{mapping.googleAccountLabel}</TableCell>
            <TableCell className="px-4 text-right">
              <DeleteMappingDialog
                id={mapping.id}
                slackChannelName={mapping.slackChannelName}
                accountLabel={mapping.googleAccountLabel}
              />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

function TableSkeleton() {
  return (
    <div className="space-y-3 p-4">
      {Array.from({ length: 4 }).map((_, index) => (
        <div key={index} className="flex items-center gap-4">
          <Skeleton className="h-5 w-40" />
          <Skeleton className="h-5 w-32" />
          <Skeleton className="h-5 flex-1" />
          <Skeleton className="size-7 rounded-md" />
        </div>
      ))}
    </div>
  );
}

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-muted">
        <Link2 className="size-6 text-muted-foreground" aria-hidden="true" />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">No mappings yet</p>
        <p className="text-sm text-muted-foreground">Create a mapping to route a channel&apos;s uploads to an account.</p>
      </div>
      <AddMappingDialog />
    </div>
  );
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <div className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <div className="flex size-12 items-center justify-center rounded-full bg-destructive/10">
        <AlertTriangle className="size-6 text-destructive" aria-hidden="true" />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">Couldn’t load mappings</p>
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
