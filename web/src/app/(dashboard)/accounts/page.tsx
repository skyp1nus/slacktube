"use client";

import { useEffect } from "react";
import { AlertTriangle, MonitorPlay, Plug } from "lucide-react";
import { toast } from "sonner";

import { useAccounts } from "@/hooks/use-accounts";
import { AccountCard } from "./account-card";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";

function startGoogleOAuth() {
  window.location.href = `${BACKEND}/google/oauth/start`;
}

function ConnectButton({ variant = "default" }: { variant?: "default" | "outline" }) {
  return (
    <Button variant={variant} onClick={startGoogleOAuth}>
      <Plug />
      Connect Google account
    </Button>
  );
}

export default function AccountsPage() {
  const { data: accounts, isPending, isError, error, refetch } = useAccounts();

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const connected = params.get("connected");
    const oauthError = params.get("error");
    if (!connected && !oauthError) return;

    if (oauthError) toast.error(`Google connect failed: ${decodeURIComponent(oauthError)}`);
    else if (connected === "1") {
      toast.success("Google account connected");
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
          <h2 className="text-2xl font-semibold tracking-tight">Accounts</h2>
          <p className="text-sm text-muted-foreground">
            Connect one or more YouTube channels. Each consent adds a new account.
          </p>
        </div>
        <ConnectButton />
      </div>

      {isPending ? (
        <AccountsSkeleton />
      ) : isError ? (
        <ErrorState
          message={error instanceof Error ? error.message : "Failed to load accounts."}
          onRetry={() => refetch()}
        />
      ) : accounts && accounts.length > 0 ? (
        <div className="space-y-4">
          {accounts.map((account) => (
            <AccountCard key={account.id} account={account} />
          ))}
          <p className="text-xs text-muted-foreground">
            Note: YouTube quota is enforced per Google Cloud project (OAuth client), not per channel —
            accounts sharing one OAuth client share the ~6 uploads/day cap.
          </p>
        </div>
      ) : (
        <EmptyState />
      )}
    </div>
  );
}

function AccountsSkeleton() {
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
          <MonitorPlay className="size-6 text-muted-foreground" aria-hidden="true" />
        </div>
        <div className="space-y-1">
          <p className="text-sm font-medium">No Google accounts connected yet</p>
          <p className="text-sm text-muted-foreground">Connect a YouTube channel account to upload videos.</p>
        </div>
        <ConnectButton />
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
          <p className="text-sm font-medium">Couldn’t load accounts</p>
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
