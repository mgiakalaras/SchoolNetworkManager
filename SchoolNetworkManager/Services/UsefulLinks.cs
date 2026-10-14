using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

public record UsefulLink(string Title, string Url, string? Group = null);

/// <summary>
/// Χρήσιμα links για την ομάδα: σταθερά εξωτερικά, τα εργαλεία του server
/// (από τις Ρυθμίσεις) και όσα προσθέσει η ομάδα στις Ρυθμίσεις → Γενικά.
/// </summary>
public static class UsefulLinks
{
    public static readonly List<UsefulLink> Defaults = new()
    {
        new("Veyon — λήψη", "https://veyon.io/en/download/", "Λογισμικό"),
        new("Veyon — εγχειρίδιο", "https://docs.veyon.io/en/latest/", "Λογισμικό"),
        new("Ninite — μαζική εγκατάσταση προγραμμάτων", "https://ninite.com/", "Λογισμικό"),
        new("Windows 11 — επίσημη λήψη (Microsoft)", "https://www.microsoft.com/el-gr/software-download/windows11", "Λογισμικό"),
        new("Windows 10 — επίσημη λήψη (Microsoft)", "https://www.microsoft.com/el-gr/software-download/windows10", "Λογισμικό")
    };

    /// <summary>Διαβάζει γραμμές της μορφής «Τίτλος | https://…».</summary>
    public static List<UsefulLink> ParseCustom(string? text)
    {
        var list = new List<UsefulLink>();

        if (string.IsNullOrWhiteSpace(text))
        {
            return list;
        }

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split('|', 2, StringSplitOptions.TrimEntries);
            var title = parts.Length == 2 ? parts[0] : parts[0];
            var url = parts.Length == 2 ? parts[1] : parts[0];

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                list.Add(new UsefulLink(string.IsNullOrWhiteSpace(title) ? uri.Host : title, uri.ToString(), "Της ομάδας"));
            }
        }

        return list;
    }

    /// <summary>Links προς τα εργαλεία του server (Pi-hole, MySpeed, ZimaOS), με τη διεύθυνση που βλέπει ο χρήστης.</summary>
    public static List<UsefulLink> ServerLinks(AppSettings settings, string requestHost)
    {
        var list = new List<UsefulLink>
        {
            new("ZimaOS", $"http://{requestHost}/", "Server")
        };

        if (!string.IsNullOrWhiteSpace(settings.PiholeUrl))
        {
            var pihole = settings.PiholeUrl.Trim().TrimEnd('/');
            if (!pihole.Contains("://")) pihole = "http://" + pihole;
            if (!pihole.EndsWith("/admin", StringComparison.OrdinalIgnoreCase)) pihole += "/admin/";
            list.Add(new UsefulLink("Pi-hole", Localize(pihole, requestHost), "Server"));
        }

        if (!string.IsNullOrWhiteSpace(settings.MySpeedUrl))
        {
            list.Add(new UsefulLink("MySpeed", Localize(settings.MySpeedUrl.Trim(), requestHost), "Server"));
        }

        return list;
    }

    /// <summary>Το 127.0.0.1 / localhost δουλεύει μόνο μέσα στον server — για τον browser το αλλάζουμε στη διεύθυνση του server.</summary>
    private static string Localize(string url, string requestHost)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url;
        }

        if (uri.IsLoopback)
        {
            var builder = new UriBuilder(uri) { Host = requestHost };
            return builder.Uri.ToString();
        }

        return uri.ToString();
    }
}
