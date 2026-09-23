using System.Net;
using System.Net.Sockets;

namespace FourFoldAccountManager.Leaderboard.Service.Api;

public static class RateLimitClientIp
{
    public static string GetPartitionKey(HttpContext context, bool trustCloudflareConnectingIp)
    {
        if (trustCloudflareConnectingIp &&
            context.Request.Headers.TryGetValue("CF-Connecting-IP", out var values) &&
            values.Count == 1)
        {
            var value = values[0];
            if (value is not null && value == value.Trim() &&
                !value.Contains('%') && !value.Contains(',') &&
                IPAddress.TryParse(value, out var address) &&
                (address.AddressFamily != AddressFamily.InterNetwork ||
                 string.Equals(value, address.ToString(), StringComparison.Ordinal)))
                return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
