"use client";

import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Loader2, Plus } from "lucide-react";

import { useCreateGoogleClient } from "@/hooks/use-google-clients";
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
import { Form, FormControl, FormDescription, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui/form";
import { Input } from "@/components/ui/input";

const formSchema = z.object({
  label: z.string().trim().max(100, "Keep it under 100 characters").optional(),
  clientId: z.string().trim().min(1, "Client id is required"),
  clientSecret: z.string().min(1, "Client secret is required"),
});
type FormValues = z.infer<typeof formSchema>;

export function AddClientDialog({ trigger }: { trigger?: React.ReactNode }) {
  const [open, setOpen] = useState(false);
  const createClient = useCreateGoogleClient();

  const form = useForm<FormValues>({
    resolver: zodResolver(formSchema),
    defaultValues: { label: "", clientId: "", clientSecret: "" },
  });

  function handleOpenChange(next: boolean) {
    if (!next && createClient.isPending) return;
    setOpen(next);
    if (!next) form.reset();
  }

  const onSubmit = form.handleSubmit((values) => {
    createClient.mutate(
      { label: values.label ?? "", clientId: values.clientId, clientSecret: values.clientSecret },
      {
        onSuccess: () => {
          form.reset();
          setOpen(false);
        },
      },
    );
  });

  return (
    <Dialog open={open} onOpenChange={handleOpenChange}>
      <DialogTrigger asChild>
        {trigger ?? (
          <Button>
            <Plus />
            Add project
          </Button>
        )}
      </DialogTrigger>
      <DialogContent className="max-h-[85vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add YouTube project</DialogTitle>
          <DialogDescription>
            One OAuth client per Google Cloud project. Each project has its own daily quota, so adding
            projects raises the per-channel upload ceiling.
          </DialogDescription>
        </DialogHeader>

        <Form {...form}>
          <form onSubmit={onSubmit} className="grid gap-4">
            <FormField
              control={form.control}
              name="label"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Label</FormLabel>
                  <FormControl>
                    <Input placeholder="e.g. Project A" {...field} />
                  </FormControl>
                  <FormDescription>Optional — defaults to the client id.</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="clientId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>OAuth client id</FormLabel>
                  <FormControl>
                    <Input placeholder="…apps.googleusercontent.com" autoComplete="off" {...field} />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />

            <FormField
              control={form.control}
              name="clientSecret"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>OAuth client secret</FormLabel>
                  <FormControl>
                    <Input type="password" placeholder="••••••••" autoComplete="new-password" {...field} />
                  </FormControl>
                  <FormDescription>Stored encrypted. It is never shown again.</FormDescription>
                  <FormMessage />
                </FormItem>
              )}
            />

            <DialogFooter>
              <Button type="button" variant="outline" onClick={() => handleOpenChange(false)} disabled={createClient.isPending}>
                Cancel
              </Button>
              <Button type="submit" disabled={createClient.isPending}>
                {createClient.isPending ? (
                  <>
                    <Loader2 className="animate-spin" />
                    Adding…
                  </>
                ) : (
                  "Add project"
                )}
              </Button>
            </DialogFooter>
          </form>
        </Form>
      </DialogContent>
    </Dialog>
  );
}
