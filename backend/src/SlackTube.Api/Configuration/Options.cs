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
    /// <summary>YouTube daily quota cap in units (default ~10000).</summary>
    public int YouTubeDailyQuotaUnits { get; set; } = 10000;
    /// <summary>Cost of one videos.insert (~1600 units).</summary>
    public int YouTubeUploadCostUnits { get; set; } = 1600;

    /// <summary>Whole uploads that still fit under today's cap.</summary>
    public int DailyUploadCapacity => YouTubeDailyQuotaUnits / YouTubeUploadCostUnits;
}
