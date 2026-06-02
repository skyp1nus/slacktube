"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { api, apiErrorMessage } from "@/lib/api";
import type { ChannelMappingDto, GoogleAccountDto, MemberChannelDto } from "@/lib/types";

export const mappingsQueryKey = ["mappings"] as const;
export const mappingOptionsQueryKey = ["mappings", "options"] as const;

/** GET /api/admin/mappings — configured channel → account routes. */
export function useMappings() {
  return useQuery({
    queryKey: mappingsQueryKey,
    queryFn: () => api.get<ChannelMappingDto[]>("/api/admin/mappings"),
  });
}

export type MappingOption = { id: string; label: string };
export type MappingOptions = {
  slackChannels: MemberChannelDto[];
  accounts: MappingOption[];
};

/** The pick lists for a new mapping: member channels + connected accounts. */
export function useMappingOptions() {
  return useQuery({
    queryKey: mappingOptionsQueryKey,
    queryFn: async (): Promise<MappingOptions> => {
      const [slackChannels, accounts] = await Promise.all([
        api.get<MemberChannelDto[]>("/api/admin/slack/channels"),
        api.get<GoogleAccountDto[]>("/api/admin/google/accounts"),
      ]);
      return {
        slackChannels,
        accounts: accounts.map((a) => ({ id: a.id, label: a.youTubeChannelTitle ?? a.label })),
      };
    },
  });
}

export type CreateMappingInput = {
  slackWorkspaceId: string;
  slackChannelId: string;
  googleAccountId: string;
};

/** POST /api/admin/mappings — create a route (409 on duplicate channel). */
export function useCreateMapping() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateMappingInput) => api.post<{ created: boolean }>("/api/admin/mappings", input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mappingsQueryKey });
      toast.success("Mapping created");
    },
    onError: (error) =>
      toast.error(
        apiErrorMessage(error, "Failed to create mapping").replace("already_mapped", "That channel is already mapped"),
      ),
  });
}

/** DELETE /api/admin/mappings/{id}. */
export function useDeleteMapping() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del<void>(`/api/admin/mappings/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: mappingsQueryKey });
      toast.success("Mapping deleted");
    },
    onError: (error) => toast.error(apiErrorMessage(error, "Failed to delete mapping")),
  });
}
