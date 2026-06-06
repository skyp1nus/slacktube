"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api";
import type { JobFilterOptions, JobsResponse } from "@/lib/types";

export type JobsParams = {
  status?: string;
  channel?: string;
  tag?: string;
  account?: string;
  from?: string;
  to?: string;
  search?: string;
  page: number;
  pageSize: number;
};

export function jobsQueryKey(params: JobsParams) {
  return ["jobs", params] as const;
}

/** GET /api/admin/jobs?status&channel&tag&account&from&to&search&page&pageSize — paginated upload-job history. */
export function useJobs(params: JobsParams) {
  return useQuery({
    queryKey: jobsQueryKey(params),
    queryFn: () => {
      const qs = new URLSearchParams();
      if (params.status) qs.set("status", params.status);
      if (params.channel) qs.set("channel", params.channel);
      if (params.tag) qs.set("tag", params.tag);
      if (params.account) qs.set("account", params.account);
      if (params.from) qs.set("from", params.from);
      if (params.to) qs.set("to", params.to);
      if (params.search) qs.set("search", params.search);
      qs.set("page", String(params.page));
      qs.set("pageSize", String(params.pageSize));
      return api.get<JobsResponse>(`/api/admin/jobs?${qs.toString()}`);
    },
    placeholderData: keepPreviousData,
  });
}

/** GET /api/admin/jobs/filters — facet options (channels/tags/accounts that appear in jobs). */
export function useJobFilters() {
  return useQuery({
    queryKey: ["jobs", "filters"],
    queryFn: () => api.get<JobFilterOptions>("/api/admin/jobs/filters"),
    staleTime: 5 * 60 * 1000,
  });
}

/**
 * Jobs created in the rolling last 24h, for the dashboard "Recent jobs" card.
 * `from` is computed at fetch time (not in the key) so the key stays stable while the window slides;
 * a 60s refetch keeps the card live.
 */
export function useRecentJobs() {
  return useQuery({
    queryKey: ["jobs", "recent24h"],
    queryFn: () => {
      const from = new Date(Date.now() - 24 * 60 * 60 * 1000).toISOString();
      const qs = new URLSearchParams({ from, pageSize: "50" });
      return api.get<JobsResponse>(`/api/admin/jobs?${qs.toString()}`);
    },
    refetchInterval: 60_000,
  });
}
