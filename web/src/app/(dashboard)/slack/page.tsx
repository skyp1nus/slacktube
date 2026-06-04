"use client";

import { useEffect, useState } from "react";
import { Bot, CalendarDays, Hash, Loader2, Lock, Plug, RefreshCw, Unplug } from "lucide-react";
import { toast } from "sonner";

import type { SlackWorkspaceDto } from "@/lib/types";
import { useRefreshChannels, useSlackWorkspaces, useWorkspaceChannels } from "@/hooks/use-slack";
import { DisconnectWorkspaceDialog } from "./disconnect-workspace-dialog";
import {
  Accordion,
  AccordionContent,
  AccordionItem,
  AccordionTrigger,
} from "@/components/ui/accordion";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";
const dateFormatter = new Intl.DateTimeFormat("en-US", { dateStyle: "medium" });

function formatDate(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? "—" : dateFormatter.format(date);
}

/** Full-page redirect into the backend's Slack OAuth start endpoint. */
function startSlackOAuth() {
  window.location.href = `${BACKEND}/slack/oauth/start`;
}

function ConnectSlackButton({ variant = "default" }: { variant?: "default" | "outline" }) {
  return (
    <Button variant={variant} onClick={startSlackOAuth}>
      <Plug />
      Connect Slack
    </Button>
  );
}

export default function SlackPage() {
  const { data: workspaces, isPending, isError, error, refetch } = useSlackWorkspaces();

  // Handle the OAuth callback flags once on mount, then strip them from the URL.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const connected = params.get("connected");
    const oauthError = params.get("error");
    if (!connected && !oauthError) return;

    if (oauthError) toast.error(`Slack connect failed: ${decodeURIComponent(oauthError)}`);
    else if (connected === "1") {
      toast.success("Slack workspace connected");
      void refetch();
    }

    params.delete("connected");
    params.delete("error");
    const query = params.toString();
    window.history.replaceState(null, "", `${window.location.pathname}${query ? `?${query}` : ""}`);
  }, [refetch]);

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h2 className="text-2xl font-semibold tracking-tight">Slack</h2>
          <p className="text-sm text-muted-foreground">
            Connect your Slack workspaces and review their channels.
          </p>
        </div>
        <ConnectSlackButton />
      </div>

      {isPending ? (
        <WorkspacesSkeleton />
      ) : isError ? (
        <ErrorState
          message={error instanceof Error ? error.message : "Failed to load Slack workspaces."}
          onRetry={() => refetch()}
        />
      ) : workspaces && workspaces.length > 0 ? (
        <div className="space-y-4">
          {workspaces.map((workspace) => (
            <WorkspaceCard key={workspace.id} workspace={workspace} />
          ))}
        </div>
      ) : (
        <EmptyState />
      )}
    </div>
  );
}

// Slack doesn't expose the workspace icon without the team:read scope, so we render a generated
// initial-avatar (first letter + a stable color picked from the team name) — like Slack/Google do.
const AVATAR_COLORS = [
  "bg-rose-500",
  "bg-orange-500",
  "bg-amber-500",
  "bg-emerald-500",
  "bg-teal-500",
  "bg-sky-500",
  "bg-indigo-500",
  "bg-violet-500",
  "bg-fuchsia-500",
];

function workspaceAvatar(name: string): { initial: string; color: string } {
  const trimmed = name.trim();
  let hash = 0;
  for (let i = 0; i < trimmed.length; i++) hash = (hash * 31 + trimmed.charCodeAt(i)) >>> 0;
  return {
    initial: trimmed.length > 0 ? trimmed.charAt(0).toUpperCase() : "?",
    color: AVATAR_COLORS[hash % AVATAR_COLORS.length] ?? "bg-slate-500",
  };
}

function WorkspaceCard({ workspace }: { workspace: SlackWorkspaceDto }) {
  const [expanded, setExpanded] = useState(false);
  const refresh = useRefreshChannels();
  const avatar = workspaceAvatar(workspace.teamName);

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3 space-y-0">
        <div className="flex items-start gap-3">
          <div
            className={`flex size-9 shrink-0 items-center justify-center rounded-lg text-sm font-semibold text-white ${avatar.color}`}
            aria-hidden="true"
          >
            {avatar.initial}
          </div>
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <CardTitle className="text-base">{workspace.teamName}</CardTitle>
              {workspace.isActive ? (
                <Badge variant="outline" className="border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400">
                  Active
                </Badge>
              ) : (
                <Badge variant="outline">Inactive</Badge>
              )}
            </div>
            <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
              <span className="inline-flex items-center gap-1.5">
                <Bot className="size-3.5" aria-hidden="true" />
                {workspace.botUserId ? <span className="font-mono">{workspace.botUserId}</span> : "No bot user"}
              </span>
              <span className="inline-flex items-center gap-1.5">
                <Hash className="size-3.5" aria-hidden="true" />
                {workspace.channelCount} {workspace.channelCount === 1 ? "channel" : "channels"}
              </span>
              <span className="inline-flex items-center gap-1.5">
                <CalendarDays className="size-3.5" aria-hidden="true" />
                Installed {formatDate(workspace.installedAt)}
              </span>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-1">
          <Button variant="outline" size="sm" disabled={refresh.isPending} onClick={() => refresh.mutate(workspace.id)}>
            {refresh.isPending ? <Loader2 className="animate-spin" /> : <RefreshCw />}
            Refresh channels
          </Button>
          <DisconnectWorkspaceDialog id={workspace.id} teamName={workspace.teamName} />
        </div>
      </CardHeader>

      <Separator />

      <CardContent className="py-0">
        <Accordion
          type="single"
          collapsible
          value={expanded ? "channels" : ""}
          onValueChange={(value) => setExpanded(value === "channels")}
        >
          <AccordionItem value="channels" className="border-b-0">
            <AccordionTrigger className="py-3">
              <span className="text-sm font-medium">
                Channels <span className="text-muted-foreground">({workspace.channelCount})</span>
              </span>
            </AccordionTrigger>
            <AccordionContent className="pb-3">
              <ChannelList workspaceId={workspace.id} enabled={expanded} />
            </AccordionContent>
          </AccordionItem>
        </Accordion>
      </CardContent>
    </Card>
  );
}

function ChannelList({ workspaceId, enabled }: { workspaceId: string; enabled: boolean }) {
  const { data: channels, isPending, isError, error } = useWorkspaceChannels(enabled ? workspaceId : null);

  if (isPending) {
    return (
      <div className="space-y-2">
        {Array.from({ length: 3 }).map((_, index) => (
          <Skeleton key={index} className="h-5 w-40" />
        ))}
      </div>
    );
  }

  if (isError) {
    return <p className="text-sm text-destructive">{error instanceof Error ? error.message : "Failed to load channels."}</p>;
  }

  if (!channels || channels.length === 0) {
    return <p className="text-sm text-muted-foreground">No channels found. Try refreshing channels.</p>;
  }

  return (
    <ul className="grid gap-1.5 sm:grid-cols-2">
      {channels.map((channel) => (
        <li key={channel.id} className="flex items-center gap-1.5 text-sm text-foreground">
          {channel.isPrivate ? (
            <Lock className="size-3.5 shrink-0 text-muted-foreground" aria-label="Private channel" />
          ) : (
            <Hash className="size-3.5 shrink-0 text-muted-foreground" aria-hidden="true" />
          )}
          <span className="truncate">{channel.name}</span>
          {!channel.isMember && <span className="text-xs text-amber-600">(bot not invited)</span>}
        </li>
      ))}
    </ul>
  );
}

function WorkspacesSkeleton() {
  return (
    <div className="space-y-4">
      {Array.from({ length: 2 }).map((_, index) => (
        <Card key={index}>
          <CardHeader className="flex flex-row items-start justify-between gap-3 space-y-0">
            <div className="flex items-start gap-3">
              <Skeleton className="size-9 rounded-lg" />
              <div className="space-y-2">
                <Skeleton className="h-5 w-40" />
                <Skeleton className="h-4 w-56" />
              </div>
            </div>
            <Skeleton className="h-7 w-44" />
          </CardHeader>
          <Separator />
          <CardContent className="py-3">
            <Skeleton className="h-5 w-28" />
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
          <Unplug className="size-6 text-muted-foreground" aria-hidden="true" />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">No Slack workspaces connected yet</p>
          <p className="text-sm text-muted-foreground">
            Connect a workspace, then invite the bot to the channels you want to upload from.
          </p>
        </div>
        <ConnectSlackButton />
      </CardContent>
    </Card>
  );
}

function ErrorState({ message, onRetry }: { message: string; onRetry: () => void }) {
  return (
    <Card>
      <CardContent className="flex flex-col items-center justify-center gap-4 px-6 py-16 text-center">
        <div className="flex size-12 items-center justify-center rounded-full bg-destructive/10">
          <Unplug className="size-6 text-destructive" aria-hidden="true" />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">Couldn’t load Slack workspaces</p>
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
