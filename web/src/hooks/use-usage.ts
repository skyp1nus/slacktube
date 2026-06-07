"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api";
import type { UsageReport } from "@/lib/types";

export const usageQueryKey = ["usage"] as const;

/** GET /api/admin/usage — today's API spend (PT day) across YouTube, Drive, and Slack. */
export function useApiUsage() {
  return useQuery({
    queryKey: usageQueryKey,
    queryFn: () => api.get<UsageReport>("/api/admin/usage"),
    refetchInterval: 30_000, // refresh while you watch
  });
}
