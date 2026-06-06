"use client";

import { AlertTriangle, Boxes } from "lucide-react";

import { useGoogleClients } from "@/hooks/use-google-clients";
import { AddClientDialog } from "./add-client-dialog";
import { ProjectCard } from "./project-card";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";

export default function ProjectsPage() {
  const { data: clients, isPending, isError, error, refetch } = useGoogleClients();

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-semibold tracking-tight">YouTube projects</h2>
          <p className="text-sm text-muted-foreground">
            One OAuth client per Google Cloud project. YouTube quota is enforced per project, so each one
            adds its own ~6 uploads/day — connect a channel through several to raise its ceiling.
          </p>
        </div>
        <AddClientDialog />
      </div>

      {isPending ? (
        <ProjectsSkeleton />
      ) : isError ? (
        <ErrorState
          message={error instanceof Error ? error.message : "Failed to load projects."}
          onRetry={() => refetch()}
        />
      ) : clients && clients.length > 0 ? (
        <div className="space-y-4">
          {clients.map((client) => (
            <ProjectCard key={client.id} client={client} />
          ))}
          <p className="text-xs text-muted-foreground">
            Every project must register the same OAuth redirect URI and enable the YouTube Data API v3 with
            the same scopes. Disabling a project skips it in upload rotation.
          </p>
        </div>
      ) : (
        <EmptyState />
      )}
    </div>
  );
}

function ProjectsSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 2 }).map((_, index) => (
        <Card key={index}>
          <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0">
            <div className="space-y-2">
              <Skeleton className="h-5 w-40" />
              <Skeleton className="h-4 w-56" />
            </div>
            <Skeleton className="h-7 w-28" />
          </CardHeader>
          <Separator />
          <CardContent className="py-3">
            <Skeleton className="h-3 w-full" />
          </CardContent>
        </Card>
      ))}
    </div>
  );
}

function EmptyState() {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
        <div className="flex size-12 items-center justify-center rounded-full bg-muted">
          <Boxes className="size-6 text-muted-foreground" aria-hidden="true" />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">No YouTube projects yet</p>
          <p className="text-sm text-muted-foreground">
            Add a Google Cloud project&apos;s OAuth client to start connecting channels.
          </p>
        </div>
        <AddClientDialog />
      </CardContent>
    </Card>
  );
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
        <div className="flex size-12 items-center justify-center rounded-full bg-destructive/10">
          <AlertTriangle className="size-6 text-destructive" aria-hidden="true" />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">Couldn’t load projects</p>
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
