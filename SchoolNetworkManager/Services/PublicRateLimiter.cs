using System.Collections.Concurrent;

namespace SchoolNetworkManager.Services;

/// <summary>Όριο για τη δημόσια φόρμα: λίγες δηλώσεις ανά υπολογιστή/κινητό, ώστε να μη «βομβαρδίζεται».</summary>
public class PublicRateLimiter
{
    private const int PerClient = 5;
    private const int Global = 80;
    private static readonly TimeSpan ClientWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan GlobalWindow = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, List<DateTime>> _hits = new();
    private readonly List<DateTime> _all = new();
    private readonly object _lock = new();

    public bool TryAcquire(string? client)
    {
        var key = string.IsNullOrWhiteSpace(client) ? "?" : client;
        var now = DateTime.UtcNow;

        lock (_lock)
        {
            _all.RemoveAll(t => now - t > GlobalWindow);

            var list = _hits.GetOrAdd(key, _ => new List<DateTime>());
            list.RemoveAll(t => now - t > ClientWindow);

            if (list.Count >= PerClient || _all.Count >= Global)
            {
                return false;
            }

            list.Add(now);
            _all.Add(now);

            if (_hits.Count > 2000)
            {
                foreach (var stale in _hits.Where(h => h.Value.Count == 0).Select(h => h.Key).ToList())
                {
                    _hits.TryRemove(stale, out _);
                }
            }

            return true;
        }
    }
}
