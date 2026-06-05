"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { api, apiErrorMessage } from "@/lib/api";
import type { SettingsDto } from "@/lib/types";

export const settingsQueryKey = ["settings"] as const;

/** GET /api/admin/settings — upload defaults (visibility + chunk size). */
export function useSettings() {
  return useQuery({
    queryKey: settingsQueryKey,
    queryFn: () => api.get<SettingsDto>("/api/admin/settings"),
  });
}

/** PATCH /api/admin/settings — partial update of upload defaults. */
export function useUpdateSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: Partial<SettingsDto>) => api.patch<SettingsDto>("/api/admin/settings", input),
    onSuccess: (data) => {
      queryClient.setQueryData(settingsQueryKey, data);
      toast.success("Settings saved");
    },
    onError: (error) => toast.error(apiErrorMessage(error, "Failed to save settings")),
  });
}
