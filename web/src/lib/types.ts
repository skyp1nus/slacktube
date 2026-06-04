/** Backend DTOs (camelCase JSON) consumed by the admin panel hooks. */

export type SlackWorkspaceDto = {
  id: string;
  slackTeamId: string;
  teamName: string;
  botUserId: string | null;
  isActive: boolean;
  installedAt: string;
  channelCount: number;
};

export type SlackChannelDto = {
  id: string;
  slackChannelId: string;
  name: string;
  isPrivate: boolean;
  isMember: boolean;
};

/** A channel the bot is a member of, across workspaces (mapping picker). */
export type MemberChannelDto = {
  id: string;
  slackChannelId: string;
  name: string;
  isPrivate: boolean;
  workspaceId: string;
  workspaceName: string;
};

export type QuotaDto = {
  usedUnits: number;
  capUnits: number;
  remainingUploads: number;
  totalUploads: number;
};

/** A connected Google/YouTube account with its per-account daily quota. */
export type GoogleAccountDto = {
  id: string;
  label: string;
  youTubeChannelId: string | null;
  youTubeChannelTitle: string | null;
  avatarUrl: string | null;
  accountEmail: string | null;
  status: string;
  createdAt: string;
  quota: QuotaDto;
};

/** A Slack-channel → Google-account route. */
export type ChannelMappingDto = {
  id: string;
  slackWorkspaceId: string;
  slackWorkspaceName: string;
  slackChannelId: string;
  slackChannelName: string;
  googleAccountId: string;
  googleAccountLabel: string;
  googleAccountAvatarUrl: string | null;
  googleAccountChannelId: string | null;
  createdAt: string;
};

/** An upload job (one Slack template message). */
export type JobDto = {
  id: string;
  fileName: string | null;
  state: string;
  youTubeUrl: string | null;
  error: string | null;
  tags: string[];
  googleAccountId: string | null;
  createdAt: string;
  updatedAt: string;
};

export type JobsResponse = { items: JobDto[]; total: number };

/** Aggregate admin dashboard KPIs (GET /api/admin/dashboard). */
export type DashboardStats = {
  workspaceCount: number;
  accountCount: number;
  uploadsToday: number;
  uploadsLast24h: number;
  errorsLast24h: number;
  quotaUsedUnits: number;
  quotaCapUnits: number;
};

/** Job lifecycle states (matches the backend JobState enum names). */
export const JOB_STATES = [
  "Queued",
  "Downloading",
  "Uploading",
  "Processing",
  "Done",
  "Cancelled",
  "Failed",
  "Blocked",
] as const;
export type JobState = (typeof JOB_STATES)[number];
