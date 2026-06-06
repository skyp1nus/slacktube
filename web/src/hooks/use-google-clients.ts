"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { accountsQueryKey } from "@/hooks/use-accounts";
import { api, apiErrorMessage } from "@/lib/api";
import type { GoogleOAuthClientDto } from "@/lib/types";

export const googleClientsQueryKey = ["google-clients"] as const;

/** GET /api/admin/google/clients — OAuth clients (Cloud projects) + per-project quota. */
export function useGoogleClients() {
  return useQuery({
    queryKey: googleClientsQueryKey,
    queryFn: () => api.get<GoogleOAuthClientDto[]>("/api/admin/google/clients"),
  });
}

export type CreateClientInput = { label: string; clientId: string; clientSecret: string };

/** POST /api/admin/google/clients — add a project (secret is encrypted server-side, never read back). */
export function useCreateGoogleClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateClientInput) =>
      api.post<GoogleOAuthClientDto>("/api/admin/google/clients", input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: googleClientsQueryKey });
      toast.success("YouTube project added");
    },
    onError: (error) =>
      toast.error(
        apiErrorMessage(error, "Failed to add project")
          .replace("client_id_and_secret_required", "Client id and secret are both required.")
          .replace("client_id_exists", "That OAuth client id is already added."),
      ),
  });
}

export type UpdateClientInput = { id: string; label?: string; status?: string };

/** PATCH /api/admin/google/clients/{id} — rename or enable/disable. */
export function useUpdateGoogleClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...patch }: UpdateClientInput) =>
      api.patch<void>(`/api/admin/google/clients/${id}`, patch),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: googleClientsQueryKey });
      queryClient.invalidateQueries({ queryKey: accountsQueryKey });
      toast.success("Project updated");
    },
    onError: (error) => toast.error(apiErrorMessage(error, "Failed to update project")),
  });
}

/** DELETE /api/admin/google/clients/{id} — 409 if any account still references it. */
export function useDeleteGoogleClient() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/api/admin/google/clients/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: googleClientsQueryKey });
      queryClient.invalidateQueries({ queryKey: accountsQueryKey });
      toast.success("Project deleted");
    },
    onError: (error) =>
      toast.error(
        apiErrorMessage(error, "Failed to delete project").replace(
          "client_in_use",
          "Accounts still use this project — disconnect them first.",
        ),
      ),
  });
}
