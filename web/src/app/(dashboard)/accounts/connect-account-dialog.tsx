"use client";

import { useState } from "react";
import { ArrowRight, Boxes, Plug } from "lucide-react";

import { useGoogleClients } from "@/hooks/use-google-clients";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";

/** Connect a YouTube channel through a chosen project. The browser navigates top-level to the backend
 * OAuth start (with ?clientId=), so the issuing client is bound to the new account. */
export function ConnectAccountDialog({ variant = "default" }: { variant?: "default" | "outline" }) {
  const [open, setOpen] = useState(false);
  const [clientId, setClientId] = useState("");
  const { data: clients, isPending } = useGoogleClients();

  const active = (clients ?? []).filter((c) => c.status === "Active");
  // Preselect when there's exactly one usable project (derived — no effect/setState needed).
  const selected = clientId || (active.length === 1 ? active[0].id : "");

  function continueToGoogle() {
    if (!selected) return;
    window.location.href = `${BACKEND}/google/oauth/start?clientId=${encodeURIComponent(selected)}`;
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant={variant}>
          <Plug />
          Connect Google account
        </Button>
      </DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Connect a YouTube channel</DialogTitle>
          <DialogDescription>
            Pick which project to consent with. The same channel can be connected through several projects
            to multiply its daily upload quota.
          </DialogDescription>
        </DialogHeader>

        {isPending ? (
          <Skeleton className="h-9 w-full" />
        ) : active.length === 0 ? (
          <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-8 text-center">
            <div className="flex size-10 items-center justify-center rounded-full bg-muted">
              <Boxes className="size-5 text-muted-foreground" aria-hidden="true" />
            </div>
            <div className="space-y-1">
              <p className="text-sm font-medium">No active YouTube project</p>
              <p className="text-sm text-muted-foreground">Add (or enable) a project before connecting a channel.</p>
            </div>
            <Button asChild variant="outline" size="sm">
              <a href="/projects">Manage projects</a>
            </Button>
          </div>
        ) : (
          <div className="space-y-1.5">
            <Label htmlFor="connect-project">Project</Label>
            <Select value={selected} onValueChange={setClientId}>
              <SelectTrigger id="connect-project" className="w-full">
                <SelectValue placeholder="Select a project" />
              </SelectTrigger>
              <SelectContent>
                {active.map((c) => (
                  <SelectItem key={c.id} value={c.id}>
                    {c.label}{" "}
                    <span className="text-muted-foreground">— {c.quota.remainingUploads} uploads left today</span>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        {active.length > 0 && (
          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)}>
              Cancel
            </Button>
            <Button onClick={continueToGoogle} disabled={!selected}>
              Continue to Google
              <ArrowRight />
            </Button>
          </DialogFooter>
        )}
      </DialogContent>
    </Dialog>
  );
}
