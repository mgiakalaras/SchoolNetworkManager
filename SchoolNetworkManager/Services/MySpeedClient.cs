using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SchoolNetworkManager.Services;

public class SpeedTest
{
    public DateTime Time { get; set; }
    public double? Download { get; set; }
    public double? Upload { get; set; }
    public double? Ping { get; set; }
}

public class SpeedResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public List<SpeedTest> Tests { get; set; } = new();
}

/// <summary>
/// Διαβάζει τις μετρήσεις ταχύτητας από το MySpeed (https://github.com/gnmyt/myspeed).
/// Κρατά τα αποτελέσματα 5 λεπτά στη μνήμη ώστε το dashboard να ανοίγει γρήγορα.
/// </summary>
public class MySpeedClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SettingsService _settings;
    private readonly object _lock = new();

    private SpeedResult? _cached;
    private DateTime _cachedAt;
    private string? _cachedKey;

    public MySpeedClient(IHttpClientFactory httpFactory, SettingsService settings)
    {
        _httpFactory = httpFactory;
        _settings = settings;
    }

    public async Task<SpeedResult> GetLastDaysAsync(int days = 7, bool force = false)
    {
        var url = _settings.Current.MySpeedUrl?.Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(url))
        {
            return new SpeedResult { Error = "Δεν έχει οριστεί διεύθυνση MySpeed (Ρυθμίσεις → Δίκτυο)." };
        }

        var key = $"{url}|{days}|{_settings.Current.MySpeedPassword}";

        lock (_lock)
        {
            if (!force && _cached != null && _cachedKey == key && DateTime.Now - _cachedAt < TimeSpan.FromMinutes(5))
            {
                return _cached;
            }
        }

        var result = await FetchAsync(url, days);

        lock (_lock)
        {
            _cached = result;
            _cachedAt = DateTime.Now;
            _cachedKey = key;
        }

        return result;
    }

    private async Task<SpeedResult> FetchAsync(string baseUrl, int days)
    {
        try
        {
            var client = _httpFactory.CreateClient("myspeed");
            client.Timeout = TimeSpan.FromSeconds(5);

            var from = DateTime.Now.Date.AddDays(-(days - 1));
            var to = DateTime.Now.Date;

            // Νεότερες εκδόσεις: εξαγωγή ανά ημερομηνίες. Παλαιότερες: απλή λίστα.
            var urls = new[]
            {
                $"{baseUrl}/api/speedtests/export?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&format=json",
                $"{baseUrl}/api/speedtests?limit={days * 24 * 6}"
            };

            string? lastError = null;

            foreach (var requestUrl in urls)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var password = _settings.Current.MySpeedPassword;

                if (!string.IsNullOrWhiteSpace(password))
                {
                    request.Headers.TryAddWithoutValidation("x-password", Uri.EscapeDataString(password));

                    if (password.All(ch => ch <= 0xFF))
                    {
                        request.Headers.TryAddWithoutValidation("password", password);
                    }
                }

                using var response = await client.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new SpeedResult { Error = "Το MySpeed ζητά κωδικό. Συμπλήρωσέ τον στις Ρυθμίσεις → Δίκτυο." };
                }

                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"Το MySpeed απάντησε {(int)response.StatusCode}.";
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var raw = JsonSerializer.Deserialize<List<RawTest>>(json, JsonOptions) ?? new List<RawTest>();

                var tests = raw
                    .Where(t => string.IsNullOrWhiteSpace(t.Error))
                    .Select(t => new SpeedTest
                    {
                        Time = ParseTime(t.Created),
                        Download = ToDouble(t.Download),
                        Upload = ToDouble(t.Upload),
                        Ping = ToDouble(t.Ping)
                    })
                    .Where(t => t.Time >= from && t.Time < to.AddDays(1))
                    .Where(t => t.Download != null || t.Upload != null)
                    .OrderBy(t => t.Time)
                    .ToList();

                return new SpeedResult { Ok = true, Tests = tests };
            }

            return new SpeedResult { Error = lastError ?? "Δεν βρέθηκαν μετρήσεις." };
        }
        catch (TaskCanceledException)
        {
            return new SpeedResult { Error = $"Το MySpeed δεν απάντησε ({baseUrl})." };
        }
        catch (HttpRequestException)
        {
            return new SpeedResult { Error = $"Δεν υπάρχει σύνδεση με το MySpeed ({baseUrl})." };
        }
        catch (Exception ex)
        {
            return new SpeedResult { Error = "Σφάλμα ανάγνωσης MySpeed: " + ex.Message };
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static DateTime ParseTime(JsonElement value)
    {
        // Το MySpeed δίνει ώρα ISO σε UTC (π.χ. 2026-10-12T21:30:00.000Z) — μετατροπή σε τοπική ώρα.
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed.LocalDateTime;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch))
        {
            var moment = epoch > 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(epoch)
                : DateTimeOffset.FromUnixTimeSeconds(epoch);

            return moment.LocalDateTime;
        }

        return DateTime.MinValue;
    }

    private static double? ToDouble(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => null
        };
    }

    private class RawTest
    {
        public JsonElement Ping { get; set; }
        public JsonElement Download { get; set; }
        public JsonElement Upload { get; set; }
        public JsonElement Created { get; set; }
        public string? Error { get; set; }
    }
}
