"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";

import { api, apiErrorMessage } from "@/lib/api";
import type { SlackChannelDto, SlackWorkspaceDto } from "@/lib/types";

export const slackWorkspacesQueryKey = ["slack-workspaces"] as const;

export function workspaceChannelsQueryKey(workspaceId: string) {
  return ["slack-workspaces", workspaceId, "channels"] as const;
}

/** GET /api/admin/slack/workspaces — connected Slack workspaces. */
export function useSlackWorkspaces() {
  return useQuery({
    queryKey: slackWorkspacesQueryKey,
    queryFn: () => api.get<SlackWorkspaceDto[]>("/api/admin/slack/workspaces"),
  });
}

/** GET /api/admin/slack/workspaces/{id}/channels — only when a workspace id is provided. */
export function useWorkspaceChannels(workspaceId: string | null | undefined) {
  return useQuery({
    queryKey: workspaceChannelsQueryKey(workspaceId ?? ""),
    queryFn: () => api.get<SlackChannelDto[]>(`/api/admin/slack/workspaces/${workspaceId}/channels`),
    enabled: Boolean(workspaceId),
  });
}

/** POST /api/admin/slack/workspaces/{id}/refresh-channels — re-sync from Slack. */
export function useRefreshChannels() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (workspaceId: string) =>
      api.post<SlackChannelDto[]>(`/api/admin/slack/workspaces/${workspaceId}/refresh-channels`),
    onSuccess: (channels, workspaceId) => {
      queryClient.setQueryData(workspaceChannelsQueryKey(workspaceId), channels);
      queryClient.invalidateQueries({ queryKey: workspaceChannelsQueryKey(workspaceId) });
      queryClient.invalidateQueries({ queryKey: slackWorkspacesQueryKey });
      toast.success(`Refreshed channels (${channels.length} found)`);
    },
    onError: (error) => toast.error(apiErrorMessage(error, "Failed to refresh channels")),
  });
}

/** DELETE /api/admin/slack/workspaces/{id} — disconnect a workspace. */
export function useDisconnectWorkspace() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (workspaceId: string) => api.del<void>(`/api/admin/slack/workspaces/${workspaceId}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: slackWorkspacesQueryKey });
      toast.success("Slack workspace disconnected");
    },
    onError: (error) => toast.error(apiErrorMessage(error, "Failed to disconnect workspace")),
  });
}
