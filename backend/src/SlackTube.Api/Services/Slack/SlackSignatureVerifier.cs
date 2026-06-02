using System.Security.Cryptography;
using System.Text;

namespace SlackTube.Api.Services.Slack;

/// <summary>
/// Verifies Slack's X-Slack-Signature over the RAW request body:
///   base = "v0:{timestamp}:{rawBody}",  expected = "v0=" + lowercase-hex(HMAC_SHA256(secret, base)).
/// Rejects requests older than 5 minutes (replay guard) and uses a constant-time compare.
/// </summary>
public sealed class SlackSignatureVerifier
{
    private const int MaxSkewSeconds = 300;

    public bool Verify(string? signingSecret, string? timestamp, string rawBody, string? signature)
    {
        if (string.IsNullOrEmpty(signingSecret) ||
            string.IsNullOrEmpty(timestamp) ||
            string.IsNullOrEmpty(signature))
            return false;

        if (!long.TryParse(timestamp, out var tsVal))
            return false;
        if (Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - tsVal) > MaxSkewSeconds)
            return false;

        var baseStr = $"v0:{timestamp}:{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var expected = "v0=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(baseStr))).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signature));
    }
}
