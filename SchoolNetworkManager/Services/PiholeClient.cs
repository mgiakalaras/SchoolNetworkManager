using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SchoolNetworkManager.Services;

public record PiholeItem(string Key, string? Name, int Count);

public class PiholeStats
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public int Version { get; set; }
    public long TotalQueries { get; set; }
    public long BlockedQueries { get; set; }
    public double PercentBlocked { get; set; }
    public List<PiholeItem> TopBlockedClients { get; set; } = new();
    public List<PiholeItem> TopBlockedDomains { get; set; } = new();
}

/// <summary>
/// Διαβάζει στατιστικά από το Pi-hole. Υποστηρίζει την έκδοση 6 (/api, με session)
/// και την παλιά έκδοση 5 (/admin/api.php, με API token).
/// Κρατά τα αποτελέσματα 2 λεπτά στη μνήμη.
/// </summary>
public class PiholeClient
{
    public const string HttpClientName = "pihole";

    private readonly IHttpClientFactory _httpFactory;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _sid;
    private string? _sidKey;
    private PiholeStats? _cached;
    private DateTime _cachedAt;
    private string? _cachedKey;

    public PiholeClient(IHttpClientFactory httpFactory, SettingsService settings)
    {
        _httpFactory = httpFactory;
        _settings = settings;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.Current.PiholeUrl);

    public async Task<PiholeStats> GetStatsAsync(int top = 6)
    {
        var baseUrl = NormalizeBase(_settings.Current.PiholeUrl);

        if (baseUrl == null)
        {
            return new PiholeStats { Error = "Δεν έχει οριστεί διεύθυνση Pi-hole (Ρυθμίσεις → Δίκτυο)." };
        }

        var password = _settings.Current.PiholePassword ?? string.Empty;
        var key = $"{baseUrl}|{password}|{top}";

        await _lock.WaitAsync();

        try
        {
            if (_cached != null && _cachedKey == key && DateTime.Now - _cachedAt < TimeSpan.FromMinutes(2))
            {
                return _cached;
            }

            var client = _httpFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(6);

            PiholeStats result;

            try
            {
                result = await TryV6Async(client, baseUrl, password, top);

                if (!result.Ok && result.Version == 0)
                {
                    var v5 = await TryV5Async(client, baseUrl, password, top);
                    if (v5.Ok || v5.Version == 5) result = v5;
                }
            }
            catch (TaskCanceledException)
            {
                result = new PiholeStats { Error = $"Το Pi-hole δεν απάντησε ({baseUrl})." };
            }
            catch (HttpRequestException)
            {
                result = new PiholeStats { Error = $"Δεν υπάρχει σύνδεση με το Pi-hole ({baseUrl})." };
            }
            catch (Exception ex)
            {
                result = new PiholeStats { Error = "Σφάλμα ανάγνωσης Pi-hole: " + ex.Message };
            }

            _cached = result;
            _cachedAt = DateTime.Now;
            _cachedKey = key;

            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ---------------- Pi-hole v6 ----------------

    private async Task<PiholeStats> TryV6Async(HttpClient client, string baseUrl, string password, int top)
    {
        var sidKey = baseUrl + "|" + password;

        if (_sid == null || _sidKey != sidKey)
        {
            var auth = await AuthenticateAsync(client, baseUrl, password);

            if (auth.Error != null)
            {
                return auth.Error;
            }

            _sid = auth.Sid;
            _sidKey = sidKey;
        }

        var summary = await GetV6Async(client, baseUrl, "/api/stats/summary");

        if (summary.Status == HttpStatusCode.Unauthorized)
        {
            // Η συνεδρία έληξε — νέα σύνδεση και ξανά.
            _sid = null;
            var auth = await AuthenticateAsync(client, baseUrl, password);

            if (auth.Error != null)
            {
                return auth.Error;
            }

            _sid = auth.Sid;
            summary = await GetV6Async(client, baseUrl, "/api/stats/summary");
        }

        if (summary.Status == HttpStatusCode.NotFound)
        {
            return new PiholeStats { Version = 0, Error = "Δεν βρέθηκε το API του Pi-hole." };
        }

        if (summary.Json == null)
        {
            return new PiholeStats { Version = 6, Error = $"Το Pi-hole απάντησε {(int)summary.Status}." };
        }

        var stats = new PiholeStats { Ok = true, Version = 6 };

        if (summary.Json.Value.TryGetProperty("queries", out var queries))
        {
            stats.TotalQueries = GetLong(queries, "total");
            stats.BlockedQueries = GetLong(queries, "blocked");
            stats.PercentBlocked = GetDouble(queries, "percent_blocked");
        }

        var clients = await GetV6Async(client, baseUrl, $"/api/stats/top_clients?blocked=true&count={top}");

        if (clients.Json?.TryGetProperty("clients", out var clientList) == true && clientList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in clientList.EnumerateArray())
            {
                var ip = GetString(item, "ip") ?? "?";
                var name = GetString(item, "name");
                stats.TopBlockedClients.Add(new PiholeItem(ip, string.IsNullOrWhiteSpace(name) ? null : name, (int)GetLong(item, "count")));
            }
        }

        var domains = await GetV6Async(client, baseUrl, $"/api/stats/top_domains?blocked=true&count={top}");

        if (domains.Json?.TryGetProperty("domains", out var domainList) == true && domainList.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in domainList.EnumerateArray())
            {
                stats.TopBlockedDomains.Add(new PiholeItem(GetString(item, "domain") ?? "?", null, (int)GetLong(item, "count")));
            }
        }

        return stats;
    }

    private static async Task<(string? Sid, PiholeStats? Error)> AuthenticateAsync(HttpClient client, string baseUrl, string password)
    {
        using var response = await client.PostAsJsonAsync(baseUrl + "/api/auth", new { password });

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, new PiholeStats { Version = 0, Error = "Δεν βρέθηκε το API v6." });
        }

        var text = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(text);

            if (doc.RootElement.TryGetProperty("session", out var session))
            {
                var valid = session.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
                var sid = GetString(session, "sid");

                if (valid)
                {
                    return (sid, null);
                }
            }
        }
        catch (JsonException)
        {
            return (null, new PiholeStats { Version = 0, Error = "Η διεύθυνση δεν φαίνεται να είναι Pi-hole." });
        }

        return (null, new PiholeStats
        {
            Version = 6,
            Error = "Λάθος κωδικός Pi-hole. Βάλε τον κωδικό του web interface ή ένα «app password» (Ρυθμίσεις → Δίκτυο)."
        });
    }

    private async Task<(HttpStatusCode Status, JsonElement? Json)> GetV6Async(HttpClient client, string baseUrl, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + path);

        if (!string.IsNullOrEmpty(_sid))
        {
            request.Headers.TryAddWithoutValidation("X-FTL-SID", _sid);
        }

        using var response = await client.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        var text = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(text);
            return (response.StatusCode, doc.RootElement.Clone());
        }
        catch (JsonException)
        {
            return (HttpStatusCode.NotFound, null);
        }
    }

    // ---------------- Pi-hole v5 ----------------

    private static async Task<PiholeStats> TryV5Async(HttpClient client, string baseUrl, string token, int top)
    {
        var auth = string.IsNullOrWhiteSpace(token) ? string.Empty : "&auth=" + Uri.EscapeDataString(token);

        var summary = await GetJsonAsync(client, $"{baseUrl}/admin/api.php?summaryRaw{auth}");

        if (summary == null || summary.Value.ValueKind != JsonValueKind.Object)
        {
            return new PiholeStats { Error = "Δεν βρέθηκε Pi-hole σε αυτή τη διεύθυνση. Παράδειγμα: http://192.168.2.114:8080" };
        }

        var stats = new PiholeStats
        {
            Ok = true,
            Version = 5,
            TotalQueries = GetLong(summary.Value, "dns_queries_today"),
            BlockedQueries = GetLong(summary.Value, "ads_blocked_today"),
            PercentBlocked = GetDouble(summary.Value, "ads_percentage_today")
        };

        var clients = await GetJsonAsync(client, $"{baseUrl}/admin/api.php?topClientsBlocked={top}{auth}");

        if (clients?.TryGetProperty("top_sources_blocked", out var sources) == true && sources.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in sources.EnumerateObject())
            {
                // μορφή: "όνομα|ip" ή σκέτη ip
                var parts = property.Name.Split('|');
                var ip = parts.Length > 1 ? parts[1] : parts[0];
                var name = parts.Length > 1 ? parts[0] : null;
                stats.TopBlockedClients.Add(new PiholeItem(ip, name, property.Value.TryGetInt32(out var c) ? c : 0));
            }
        }
        else if (clients == null || !clients.Value.TryGetProperty("top_sources_blocked", out _))
        {
            if (stats.TotalQueries == 0 && string.IsNullOrWhiteSpace(token))
            {
                stats.Error = "Το Pi-hole 5 χρειάζεται API token (Settings → API → Show API token).";
            }
        }

        var items = await GetJsonAsync(client, $"{baseUrl}/admin/api.php?topItems={top}{auth}");

        if (items?.TryGetProperty("top_ads", out var ads) == true && ads.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in ads.EnumerateObject())
            {
                stats.TopBlockedDomains.Add(new PiholeItem(property.Name, null, property.Value.TryGetInt32(out var c) ? c : 0));
            }
        }

        return stats;
    }

    private static async Task<JsonElement?> GetJsonAsync(HttpClient client, string url)
    {
        using var response = await client.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var text = await response.Content.ReadAsStringAsync();

        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------- Βοηθητικά ----------------

    private static string? NormalizeBase(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var trimmed = url.Trim().TrimEnd('/');

        // Αν επικολλήθηκε η διεύθυνση της σελίδας διαχείρισης, κρατάμε μόνο τη ρίζα.
        foreach (var suffix in new[] { "/admin/index.php", "/admin/login", "/admin", "/api" })
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - suffix.Length);
                break;
            }
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "http://" + trimmed;
        }

        return trimmed;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long GetLong(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var l) => l,
            JsonValueKind.Number => (long)value.GetDouble(),
            JsonValueKind.String when long.TryParse(value.GetString()?.Replace(",", ""), out var s) => s,
            _ => 0
        };
    }

    private static double GetDouble(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
            _ => 0
        };
    }
}
