namespace SlackTube.Api.Configuration;

/// <summary>Slack credentials. May be supplied via config/env OR set from the admin panel
/// (stored encrypted in the DB; see SettingsStore for the resolution order).</summary>
public sealed class SlackOptions
{
    public const string Section = "Slack";
    /// <summary>App-level signing secret (env only — OAuth install does not return it).</summary>
    public string? SigningSecret { get; set; }
    // OAuth v2 install app credentials.
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Must match an app-configured redirect URL exactly.</summary>
    public string RedirectUri { get; set; } = "";
}

/// <summary>Google OAuth client (one consent covers Drive readonly + YouTube upload).</summary>
public sealed class GoogleOptions
{
    public const string Section = "Google";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Must byte-for-byte match an Authorized redirect URI in the Google console.</summary>
    public string RedirectUri { get; set; } = "";
}

/// <summary>Single-admin login for the panel + the shared key the Next.js BFF uses to call
/// the backend admin API server-side.</summary>
public sealed class AdminOptions
{
    public const string Section = "Admin";
    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "";
    /// <summary>Guards /api/admin/*. Falls back to <see cref="Password"/> when empty.</summary>
    public string? ApiKey { get; set; }
    public string EffectiveApiKey => string.IsNullOrWhiteSpace(ApiKey) ? Password : ApiKey!;
}

/// <summary>Master passphrase feeding ASP.NET Data Protection (encrypts refresh token +
/// Slack secrets at rest). Changing it makes previously stored secrets undecryptable.</summary>
public sealed class TokenEncryptionOptions
{
    public const string Section = "TokenEncryption";
    public string Key { get; set; } = "";
}

public sealed class AppOptions
{
    public const string Section = "App";
    public string PublicBaseUrl { get; set; } = "http://localhost:5080";
    /// <summary>Web admin panel origin — OAuth callbacks bounce the browser back here.</summary>
    public string AdminPanelUrl { get; set; } = "http://localhost:3000";
    public string TempDownloadDir { get; set; } = "./tmp";
    /// <summary>Per-project daily upload ceiling: <c>videos.insert</c> has its OWN daily bucket (Google's
    /// default is 100 calls/project/day), separate from the unit pool below. This — not the unit math — is
    /// what gates how many videos a single OAuth client can upload per Pacific-Time day.</summary>
    public int YouTubeDailyUploadLimit { get; set; } = 100;
    /// <summary>Daily quota for all NON-upload endpoints (list/search/etc.) in units (Google default ~10000).
    /// Uploads do not draw from this pool; it is surfaced only as an informational meter.</summary>
    public int YouTubeDailyQuotaUnits { get; set; } = 10000;
    /// <summary>Reference daily Drive API query ceiling per project for the usage display (Google's default is
    /// effectively ~1,000,000,000/day). Informational only — Drive calls are never gated.</summary>
    public long DriveDailyQueryLimit { get; set; } = 1_000_000_000;

    /// <summary>Drive download + YouTube upload chunk size in MB. Bigger = fewer HTTP round-trips
    /// (faster on large files / high-latency links) at the cost of more RAM and bigger re-sends on a
    /// transient error. Every whole MB is a valid multiple of YouTube's 256 KB chunk requirement.</summary>
    public int TransferChunkSizeMb { get; set; } = 64;

    /// <summary>Transfer chunk size in bytes (clamped to ≥1 MB).</summary>
    public int TransferChunkSizeBytes => (TransferChunkSizeMb < 1 ? 1 : TransferChunkSizeMb) * 1024 * 1024;
}
