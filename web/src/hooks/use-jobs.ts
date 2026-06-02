"use client";

import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api";
import type { JobsResponse } from "@/lib/types";

export type JobsParams = { status?: string; page: number; pageSize: number };

export function jobsQueryKey(params: JobsParams) {
  return ["jobs", params] as const;
}

/** GET /api/admin/jobs?status&page&pageSize — paginated upload-job history. */
export function useJobs(params: JobsParams) {
  return useQuery({
    queryKey: jobsQueryKey(params),
    queryFn: () => {
      const qs = new URLSearchParams();
      if (params.status) qs.set("status", params.status);
      qs.set("page", String(params.page));
      qs.set("pageSize", String(params.pageSize));
      return api.get<JobsResponse>(`/api/admin/jobs?${qs.toString()}`);
    },
    placeholderData: keepPreviousData,
  });
}

/** Latest few jobs for the dashboard "Recent jobs" card. */
export function useRecentJobs() {
  return useQuery({
    queryKey: ["jobs", "recent"],
    queryFn: () => api.get<JobsResponse>("/api/admin/jobs?pageSize=5"),
  });
}
