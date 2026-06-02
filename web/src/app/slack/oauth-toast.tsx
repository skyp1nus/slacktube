"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";

/** Surfaces the ?connected=1 / ?error= flags Slack's OAuth callback bounces back with. */
export function OAuthToast() {
  const router = useRouter();
  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const connected = params.get("connected");
    const error = params.get("error");
    if (!connected && !error) return;

    if (error) toast.error(`Slack connect failed: ${decodeURIComponent(error)}`);
    else if (connected === "1") {
      toast.success("Slack workspace connected");
      router.refresh();
    }

    params.delete("connected");
    params.delete("error");
    const q = params.toString();
    window.history.replaceState(null, "", `${window.location.pathname}${q ? `?${q}` : ""}`);
  }, [router]);

  return null;
}
