"use client";

import { useState } from "react";
import { Loader2, Trash2 } from "lucide-react";

import { useDeleteMapping } from "@/hooks/use-mappings";
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

export function DeleteMappingDialog({
  id,
  slackChannelName,
  accountLabel,
}: {
  id: string;
  slackChannelName: string;
  accountLabel: string;
}) {
  const [open, setOpen] = useState(false);
  const deleteMapping = useDeleteMapping();

  function handleOpenChange(next: boolean) {
    if (!next && deleteMapping.isPending) return;
    setOpen(next);
  }

  return (
    <AlertDialog open={open} onOpenChange={handleOpenChange}>
      <AlertDialogTrigger asChild>
        <Button variant="ghost" size="icon-sm" aria-label={`Delete mapping for #${slackChannelName}`}>
          <Trash2 className="text-destructive" />
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Delete mapping?</AlertDialogTitle>
          <AlertDialogDescription>
            This stops routing uploads from{" "}
            <span className="font-medium text-foreground">#{slackChannelName}</span> to{" "}
            <span className="font-medium text-foreground">{accountLabel}</span>. This action cannot be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel disabled={deleteMapping.isPending}>Cancel</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            disabled={deleteMapping.isPending}
            onClick={(event) => {
              event.preventDefault();
              deleteMapping.mutate(id, { onSuccess: () => setOpen(false) });
            }}
          >
            {deleteMapping.isPending ? (
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
