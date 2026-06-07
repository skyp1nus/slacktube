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

/** Two separate per-project/day buckets: the upload bucket (videos.insert calls — the real daily gate)
 * and the unit pool (non-upload endpoints — an informational meter). They do not share a budget. */
export type QuotaDto = {
  usedUploads: number;
  uploadLimit: number;
  remainingUploads: number;
  totalUploads: number;
  usedUnits: number;
  capUnits: number;
};

/** A YouTube/Google OAuth client = one Google Cloud project. Its daily quota is the real cap,
 * shared by every account that consented through it. The client secret is write-only (never read). */
export type GoogleOAuthClientDto = {
  id: string;
  label: string;
  clientId: string;
  status: string; // "Active" | "Disabled"
  createdAt: string;
  updatedAt: string;
  accountCount: number;
  quota: QuotaDto;
};

/** A connected Google/YouTube account. Its quota is the ISSUING client's shared daily cap. */
export type GoogleAccountDto = {
  id: string;
  label: string;
  youTubeChannelId: string | null;
  youTubeChannelTitle: string | null;
  avatarUrl: string | null;
  accountEmail: string | null;
  status: string;
  createdAt: string;
  oAuthClientId: string | null;
  oAuthClientLabel: string | null;
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

export type Visibility = "private" | "unlisted" | "public";

/** Upload defaults editable from the Settings tab. */
export type SettingsDto = {
  defaultVisibility: Visibility;
  transferChunkSizeMb: number;
};

/** An upload job (one Slack template message). */
export type JobDto = {
  id: string;
  fileName: string | null;
  state: string;
  youTubeUrl: string | null;
  error: string | null;
  tags: string[];
  slackChannelId: string;
  channelName: string | null;
  googleAccountId: string | null;
  googleAccountLabel: string | null;
  createdAt: string;
  updatedAt: string;
};

export type JobsResponse = { items: JobDto[]; total: number };

/** Facet options for the Jobs filter bar — only values that actually appear in jobs. */
export type JobFilterOptions = {
  channels: { id: string; name: string }[];
  tags: string[];
  accounts: { id: string; label: string }[];
};

/** Aggregate admin dashboard KPIs (GET /api/admin/dashboard). */
export type DashboardStats = {
  workspaceCount: number;
  accountCount: number;
  clientCount?: number;
  uploadsToday: number;
  uploadsLast24h: number;
  errorsLast24h: number;
  /** Upload bucket (the real daily gate): videos.insert calls used / cap, summed across projects. */
  quotaUploadsUsed: number;
  quotaUploadCap: number;
  /** Unit pool (non-upload endpoints) — informational meter, summed across projects. */
  quotaUsedUnits: number;
  quotaCapUnits: number;
};

/** Daily API-usage report (GET /api/admin/usage) — spend across every external API, resets PT midnight. */
export type UsageScope = { scope: string; used: number; limit: number | null };
export type UsageMetric = {
  key: string;
  label: string;
  unit: string; // "uploads" | "units" | "queries" | "bytes" | "calls"
  used: number;
  limit: number | null;
  perScope: UsageScope[];
};
export type UsageGroup = { group: string; metrics: UsageMetric[] };
export type UsageReport = { date: string; groups: UsageGroup[] };

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
