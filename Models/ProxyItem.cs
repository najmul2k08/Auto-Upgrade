using System;
using System.Net;

namespace AutoUpgrade.Models;

public class ProxyItem
{
    public string Raw { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 80;
    public string? Username { get; set; }
    public string? Password { get; set; }

    public static bool TryParse(string input, out ProxyItem? proxy)
    {
        proxy = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string trimmed = input.Trim();

        try
        {
            // Format: http://user:pass@host:port or http://host:port
            if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(trimmed);
                var item = new ProxyItem
                {
                    Raw = trimmed,
                    Host = uri.Host,
                    Port = uri.Port
                };

                if (!string.IsNullOrEmpty(uri.UserInfo))
                {
                    var userParts = uri.UserInfo.Split(':', 2);
                    item.Username = Uri.UnescapeDataString(userParts[0]);
                    if (userParts.Length > 1)
                    {
                        item.Password = Uri.UnescapeDataString(userParts[1]);
                    }
                }
                proxy = item;
                return true;
            }

            // Format: host:port:user:pass
            var parts = trimmed.Split(':');
            if (parts.Length == 4)
            {
                if (int.TryParse(parts[1], out int port))
                {
                    proxy = new ProxyItem
                    {
                        Raw = trimmed,
                        Host = parts[0].Trim(),
                        Port = port,
                        Username = parts[2].Trim(),
                        Password = parts[3].Trim()
                    };
                    return true;
                }
            }
            // Format: host:port
            else if (parts.Length == 2)
            {
                if (int.TryParse(parts[1], out int port))
                {
                    proxy = new ProxyItem
                    {
                        Raw = trimmed,
                        Host = parts[0].Trim(),
                        Port = port
                    };
                    return true;
                }
            }
        }
        catch
        {
            // Ignore parsing failures
        }

        return false;
    }

    public IWebProxy ToWebProxy()
    {
        var webProxy = new WebProxy(Host, Port)
        {
            BypassProxyOnLocal = false
        };

        if (!string.IsNullOrEmpty(Username))
        {
            webProxy.Credentials = new NetworkCredential(Username, Password ?? string.Empty);
        }

        return webProxy;
    }

    public override string ToString()
    {
        if (!string.IsNullOrEmpty(Username))
        {
            return $"{Host}:{Port} ({Username})";
        }
        return $"{Host}:{Port}";
    }
}

