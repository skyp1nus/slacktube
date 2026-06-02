"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api";
import type { DashboardStats } from "@/lib/types";

export const dashboardQueryKey = ["dashboard"] as const;

/** GET /api/admin/dashboard — aggregate KPIs. */
export function useDashboardStats() {
  return useQuery({
    queryKey: dashboardQueryKey,
    queryFn: () => api.get<DashboardStats>("/api/admin/dashboard"),
  });
}
