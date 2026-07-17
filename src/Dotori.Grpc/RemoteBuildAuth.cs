using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Dotori.Grpc;

/// <summary>Shared token policy for the Coordinator and Worker gRPC endpoints.</summary>
public static class RemoteBuildAuth
{
    public const string HeaderName = "x-dotori-token";

    public static string? ResolveToken(string? directToken, string? tokenFile)
    {
        if (!string.IsNullOrWhiteSpace(directToken)) return directToken;
        if (string.IsNullOrWhiteSpace(tokenFile) || !File.Exists(tokenFile)) return null;
        var value = File.ReadAllText(tokenFile).Trim();
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// A configured token is always required. Without one, only loopback callers
    /// are accepted so an accidentally exposed development process is not public.
    /// </summary>
    public static bool IsAuthorized(
        string? expectedToken,
        string? presentedToken,
        IPAddress? remoteAddress,
        bool isHttps = false,
        bool requireHttps = false)
    {
        if (requireHttps && !isHttps) return false;
        if (string.IsNullOrWhiteSpace(expectedToken))
            return remoteAddress is not null && IPAddress.IsLoopback(remoteAddress);
        if (string.IsNullOrEmpty(presentedToken)) return false;

        var expected = Encoding.UTF8.GetBytes(expectedToken);
        var presented = Encoding.UTF8.GetBytes(presentedToken);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }
}
