"use client";

import { useState } from "react";
import { Loader2, Unplug } from "lucide-react";

import { useDisconnectAccount } from "@/hooks/use-accounts";
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
import { Button } from "@/components/ui/button";

export function DisconnectAccountDialog({ id, label }: { id: string; label: string }) {
  const [open, setOpen] = useState(false);
  const disconnect = useDisconnectAccount();

  function handleOpenChange(next: boolean) {
    if (!next && disconnect.isPending) return;
    setOpen(next);
  }

  return (
    <AlertDialog open={open} onOpenChange={handleOpenChange}>
      <AlertDialogTrigger asChild>
        <Button variant="ghost" size="sm">
          <Unplug className="text-destructive" />
          Disconnect
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Disconnect account?</AlertDialogTitle>
          <AlertDialogDescription>
            This removes <span className="font-medium text-foreground">{label}</span>. If a channel
            mapping still points to it, remove that mapping first. This action cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={disconnect.isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={disconnect.isPending}
            onClick={(event) => {
              event.preventDefault();
              disconnect.mutate(id, { onSuccess: () => setOpen(false) });
            }}
          >
            {disconnect.isPending ? (
              <>
                <Loader2 className="animate-spin" />
                Disconnecting…
              </>
            ) : (
              "Disconnect"
            )}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
