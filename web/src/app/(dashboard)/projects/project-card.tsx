"use client";

import { useState } from "react";
import { CalendarDays, Loader2, Pencil, Trash2 } from "lucide-react";

import { useDeleteGoogleClient, useUpdateGoogleClient } from "@/hooks/use-google-clients";
import type { GoogleOAuthClientDto } from "@/lib/types";
import { cn } from "@/lib/utils";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Progress } from "@/components/ui/progress";
import { Separator } from "@/components/ui/separator";
import { Switch } from "@/components/ui/switch";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

const nf = new Intl.NumberFormat("en-US");
const dateFormatter = new Intl.DateTimeFormat("en-US", { dateStyle: "medium" });

function usagePercent(used: number, limit: number): number {
  if (limit <= 0) return 0;
  return Math.min(100, Math.max(0, Math.round((used / limit) * 100)));
}

function maskClientId(clientId: string): string {
  // Not a secret, but trim the long random middle for a tidy display.
  if (clientId.length <= 24) return clientId;
  return `${clientId.slice(0, 12)}…${clientId.slice(-12)}`;
}

export function ProjectCard({ client }: { client: GoogleOAuthClientDto }) {
  const update = useUpdateGoogleClient();
  const { usedUploads, uploadLimit, remainingUploads, totalUploads, usedUnits, capUnits } = client.quota;
  // The videos.insert daily bucket is the real cap — drive the bar off uploads, not the unit pool.
  const percent = usagePercent(usedUploads, uploadLimit);
  const active = client.status === "Active";
  const referenced = client.accountCount > 0;

  return (
    <Card className={cn(!active && "opacity-80")}>
      <CardHeader className="flex flex-row flex-wrap items-start justify-between gap-3 space-y-0">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <CardTitle className="text-base">{client.label}</CardTitle>
            <Badge
              variant="outline"
              className={cn(
                active && "border-green-600/30 bg-green-600/10 text-green-700 dark:text-green-400",
              )}
            >
              {client.status}
            </Badge>
          </div>
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
            <span className="font-mono" title={client.clientId}>
              {maskClientId(client.clientId)}
            </span>
            <span>
              {client.accountCount} account{client.accountCount === 1 ? "" : "s"}
            </span>
            <span className="inline-flex items-center gap-1.5">
              <CalendarDays className="size-3.5" aria-hidden="true" />
              Added {dateFormatter.format(new Date(client.createdAt))}
            </span>
          </div>
        </div>

        <div className="flex items-center gap-1">
          <label className="mr-1 flex items-center gap-2 text-xs text-muted-foreground">
            <Switch
              checked={active}
              disabled={update.isPending}
              onCheckedChange={(checked) =>
                update.mutate({ id: client.id, status: checked ? "Active" : "Disabled" })
              }
              aria-label={active ? "Disable project" : "Enable project"}
            />
            Enabled
          </label>
          <RenameDialog id={client.id} currentLabel={client.label} />
          <DeleteButton id={client.id} label={client.label} disabled={referenced} />
        </div>
      </CardHeader>

      <Separator />

      <CardContent className="space-y-1.5 py-3">
        <div className="flex items-center justify-between text-xs">
          <span className="text-muted-foreground">Uploads today (PT)</span>
          <span className={cn("font-medium tabular-nums", percent >= 90 && "text-destructive")}>{percent}%</span>
        </div>
        <Progress value={percent} className={cn(percent >= 90 && "[&>[data-slot=progress-indicator]]:bg-destructive")} />
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

function RenameDialog({ id, currentLabel }: { id: string; currentLabel: string }) {
  const [open, setOpen] = useState(false);
  const [label, setLabel] = useState(currentLabel);
  const update = useUpdateGoogleClient();

  function handleOpenChange(next: boolean) {
    if (!next && update.isPending) return;
    setOpen(next);
    if (next) setLabel(currentLabel);
  }

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Rename project">
          <Pencil />
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Rename project</DialogTitle>
        </DialogHeader>
        <div className="space-y-1.5">
          <Label htmlFor={`label-${id}`}>Label</Label>
          <Input
            id={`label-${id}`}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            maxLength={100}
            autoFocus
          />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => handleOpenChange(false)} disabled={update.isPending}>
            Cancel
          </Button>
          <Button
            disabled={update.isPending || label.trim().length === 0 || label.trim() === currentLabel}
            onClick={() =>
              update.mutate({ id, label: label.trim() }, { onSuccess: () => setOpen(false) })
            }
          >
            {update.isPending ? (
              <>
                <Loader2 className="animate-spin" />
                Saving…
              </>
            ) : (
              "Save"
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function DeleteButton({ id, label, disabled }: { id: string; label: string; disabled: boolean }) {
  const [open, setOpen] = useState(false);
  const remove = useDeleteGoogleClient();

  // Disabled while accounts reference the project — explain why via a tooltip.
  if (disabled) {
    return (
      <TooltipProvider>
        <Tooltip>
          <TooltipTrigger asChild>
            {/* span wrapper so the tooltip still fires on a disabled button */}
            <span tabIndex={0}>
              <Button variant="ghost" size="icon" disabled aria-label="Delete project">
                <Trash2 className="text-destructive" />
              </Button>
            </span>
          </TooltipTrigger>
          <TooltipContent>Disconnect its accounts first</TooltipContent>
        </Tooltip>
      </TooltipProvider>
    );
  }

  function handleOpenChange(next: boolean) {
    if (!next && remove.isPending) return;
    setOpen(next);
  }

  return (
    <AlertDialog open={open} onOpenChange={handleOpenChange}>
      <AlertDialogTrigger asChild>
        <Button variant="ghost" size="icon" aria-label="Delete project">
          <Trash2 className="text-destructive" />
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete project?</AlertDialogTitle>
          <AlertDialogDescription>
            This removes <span className="font-medium text-foreground">{label}</span> and its encrypted
            secret. This action cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={remove.isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={remove.isPending}
            onClick={(event) => {
              event.preventDefault();
              remove.mutate(id, { onSuccess: () => setOpen(false) });
            }}
          >
            {remove.isPending ? (
              <>
                <Loader2 className="animate-spin" />
                Deleting…
              </>
            ) : (
              "Delete"
            )}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
