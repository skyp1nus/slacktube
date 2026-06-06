"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertTriangle, Hash, Loader2, Lock, Plus } from "lucide-react";

import { useCreateMapping, useMappingOptions } from "@/hooks/use-mappings";
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
import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";

const formSchema = z.object({
  // value is "workspaceId:slackChannelId" so we can resolve both backend fields
  slackChannel: z.string().min(1, "Select a Slack channel"),
  googleAccountId: z.string().min(1, "Select a Google account"),
});
type FormValues = z.infer<typeof formSchema>;

export function AddMappingDialog({ trigger }: { trigger?: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  const createMapping = useCreateMapping();
  const { data: options, isPending: optionsPending } = useMappingOptions();

  const form = useForm<FormValues>({
    resolver: zodResolver(formSchema),
    defaultValues: { slackChannel: "", googleAccountId: "" },
  });

  function handleOpenChange(next: boolean) {
    if (!next && createMapping.isPending) return;
    setOpen(next);
    if (!next) form.reset();
  }

  const onSubmit = form.handleSubmit((values) => {
    const [slackWorkspaceId, slackChannelId] = values.slackChannel.split(":");
    createMapping.mutate(
      { slackWorkspaceId, slackChannelId, googleAccountId: values.googleAccountId },
      {
        onSuccess: () => {
          form.reset();
          setOpen(false);
        },
      },
    );
  });

  const noChannels = !optionsPending && (options?.slackChannels.length ?? 0) === 0;
  const noAccounts = !optionsPending && (options?.accounts.length ?? 0) === 0;
  const blocked = noChannels || noAccounts;
  const submitDisabled = createMapping.isPending || optionsPending || blocked;

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        {trigger ?? (
          <Button>
            <Plus />
            Add mapping
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add mapping</DialogTitle>
          <DialogDescription>Route a Slack channel&apos;s uploads to a Google account.</DialogDescription>
        </DialogHeader>

        {optionsPending ? (
          <div className="grid gap-4">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : blocked ? (
          <PrerequisiteHint noChannels={noChannels} noAccounts={noAccounts} />
        ) : (
          <Form {...form}>
            <form onSubmit={onSubmit} className="grid gap-4">
              <FormField
                control={form.control}
                name="slackChannel"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Slack channel</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger className="w-full">
                          <SelectValue placeholder="Select a Slack channel" />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {options?.slackChannels.map((channel) => (
                          <SelectItem key={channel.id} value={`${channel.workspaceId}:${channel.slackChannelId}`}>
                            {channel.isPrivate ? <Lock aria-hidden="true" /> : <Hash aria-hidden="true" />}
                            {channel.workspaceName} / #{channel.name}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <FormField
                control={form.control}
                name="googleAccountId"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Google account</FormLabel>
                    <Select value={field.value} onValueChange={field.onChange}>
                      <FormControl>
                        <SelectTrigger className="w-full">
                          <SelectValue placeholder="Select a Google account" />
                        </SelectTrigger>
                      </FormControl>
                      <SelectContent>
                        {options?.accounts.map((account) => (
                          <SelectItem key={account.id} value={account.id}>
                            {account.label}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <FormMessage />
                  </FormItem>
                )}
              />

              <DialogFooter>
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => handleOpenChange(false)}
                  disabled={createMapping.isPending}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={submitDisabled}>
                  {createMapping.isPending ? (
                    <>
                      <Loader2 className="animate-spin" />
                      Creating…
                    </>
                  ) : (
                    "Create mapping"
                  )}
                </Button>
              </DialogFooter>
            </form>
          </Form>
        )}
      </DialogContent>
    </Dialog>
  );
}

function PrerequisiteHint({ noChannels, noAccounts }: { noChannels: boolean; noAccounts: boolean }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-8 text-center">
      <div className="flex size-10 items-center justify-center rounded-full bg-muted">
        <AlertTriangle className="size-5 text-muted-foreground" aria-hidden="true" />
      </div>
      <div className="space-y-1">
        <p className="text-sm font-medium">Nothing to map yet</p>
        <p className="text-sm text-muted-foreground">
          {noChannels && noAccounts
            ? "Connect a Slack workspace (invite the bot to a channel) and a Google account first."
            : noChannels
              ? "Connect a Slack workspace and invite the bot to a channel first."
              : "Connect a Google account first."}
        </p>
      </div>
      <div className="flex flex-wrap justify-center gap-2">
        {noChannels && (
          <Button asChild variant="outline" size="sm">
            <a href="/slack">Connect Slack</a>
          </Button>
        )}
        {noAccounts && (
          <Button asChild variant="outline" size="sm">
            <a href="/youtube-account">Connect Google</a>
          </Button>
        )}
      </div>
    </div>
  );
}
