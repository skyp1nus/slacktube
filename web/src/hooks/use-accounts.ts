"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { api, apiErrorMessage } from "@/lib/api";
import type { GoogleAccountDto } from "@/lib/types";

export const accountsQueryKey = ["google-accounts"] as const;

/** GET /api/admin/google/accounts — connected Google accounts + per-account quota. */
export function useAccounts() {
  return useQuery({
    queryKey: accountsQueryKey,
    queryFn: () => api.get<GoogleAccountDto[]>("/api/admin/google/accounts"),
  });
}

/** DELETE /api/admin/google/accounts/{id} — disconnect (409 if a mapping still points to it). */
export function useDisconnectAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/api/admin/google/accounts/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountsQueryKey });
      toast.success("Account disconnected");
    },
    onError: (error) =>
      toast.error(apiErrorMessage(error, "Failed to disconnect account").replace("account_mapped", "A mapping still points to this account — remove it first.")),
  });
}
