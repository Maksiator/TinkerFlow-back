using System.Net;

namespace TinkerFlow.API.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// Pobiera poprawny, czytelny adres IP klienta.
    /// Rozwiązuje problem adresów IPv4 zmapowanych na IPv6 (np. ::ffff:192.168.1.1 -> 192.168.1.1)
    /// oraz obsługuje nagłówek X-Forwarded-For w przypadku proxy (Nginx, Traefik, Docker).
    /// </summary>
    public static string GetClientIpAddress(this HttpContext context)
    {
        // 1. Sprawdzamy nagłówek X-Forwarded-For (jeśli ruch przechodzi przez reverse proxy)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrWhiteSpace(forwardedFor))
        {
            var rawIp = forwardedFor.ToString().Split(',')[0].Trim();
            if (IPAddress.TryParse(rawIp, out var parsedForwardedIp))
            {
                return (parsedForwardedIp.IsIPv4MappedToIPv6 ? parsedForwardedIp.MapToIPv4() : parsedForwardedIp).ToString();
            }
            return rawIp;
        }

        // 2. Bezpośredni adres IP z połączenia
        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp == null)
        {
            return "unknown";
        }

        // 3. Konwersja IPv4 zmapowanego w IPv6 (::ffff:x.x.x.x -> x.x.x.x)
        if (remoteIp.IsIPv4MappedToIPv6)
        {
            return remoteIp.MapToIPv4().ToString();
        }

        return remoteIp.ToString();
    }
}
