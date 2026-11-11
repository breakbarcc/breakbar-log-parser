using System.Security.Cryptography;
using System.Text;
using Breakbar.Api.Options;

namespace Breakbar.Api.Identity;

/// <summary>
/// Who is calling, used as the fair-queue key and to scope job lists. No accounts (docs/PLAN.md 3):
/// - service clients (voidlog) send "X-Api-Key" and become "svc:&lt;name&gt;",
/// - browsers get an anonymous random id in the "bb_uid" cookie (128 bit, unguessable).
/// </summary>
public sealed class UserIdentity(ServiceClientOptions serviceClients)
{
    public const string CookieName = "bb_uid";
    public const string ApiKeyHeader = "X-Api-Key";
    private const string ServicePrefix = "svc:";

    /// <summary>Returns the caller's user id, issuing a cookie if needed; null if an invalid API key was sent.</summary>
    public string? Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ApiKeyHeader, out var apiKey))
        {
            return FindServiceClient(apiKey.ToString()) is { } name ? ServicePrefix + name : null;
        }

        if (context.Request.Cookies.TryGetValue(CookieName, out var existing) && IsValidAnonymousId(existing))
        {
            return existing;
        }

        string id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        context.Response.Cookies.Append(CookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365),
            IsEssential = true, // technically required to show the user their own uploads
        });
        return id;
    }

    public static bool IsServiceClient(string userId) => userId.StartsWith(ServicePrefix, StringComparison.Ordinal);

    public bool HasValidApiKey(HttpContext context)
        => context.Request.Headers.TryGetValue(ApiKeyHeader, out var key) && FindServiceClient(key.ToString()) is not null;

    private string? FindServiceClient(string apiKey)
    {
        byte[] given = Encoding.UTF8.GetBytes(apiKey);
        foreach (var (name, key) in serviceClients.ApiKeys)
        {
            if (!string.IsNullOrEmpty(key) && CryptographicOperations.FixedTimeEquals(given, Encoding.UTF8.GetBytes(key)))
            {
                return name;
            }
        }
        return null;
    }

    private static bool IsValidAnonymousId(string value)
        => value.Length == 32 && value.All(char.IsAsciiHexDigitLower);
}
