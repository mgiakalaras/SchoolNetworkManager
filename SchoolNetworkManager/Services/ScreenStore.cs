using System.Collections.Concurrent;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Προσωρινή (μόνο στη μνήμη) αποθήκη μικρογραφιών οθόνης για τον «τοίχο οθονών».
/// Τίποτα δεν γράφεται σε δίσκο ή βάση — οι εικόνες σβήνουν με την επανεκκίνηση του server
/// και όταν περάσει λίγη ώρα. Οι agents στέλνουν μικρογραφία ΜΟΝΟ όσο κάποιος βλέπει τον τοίχο.
/// </summary>
public class ScreenStore
{
    public sealed record Shot(byte[] Data, string ContentType, DateTime At);

    private readonly ConcurrentDictionary<int, Shot> _shots = new();
    private long _watchedUntilTicks;

    /// <summary>Πόσο «φρέσκια» θεωρείται μια μικρογραφία.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    /// <summary>Οι agents τραβούν οθόνη για λίγο ακόμη αφότου σταματήσει να βλέπει κάποιος.</summary>
    public static readonly TimeSpan WatchWindow = TimeSpan.FromSeconds(20);

    public void Put(int computerId, byte[] data, string contentType)
    {
        if (computerId <= 0 || data.Length == 0)
        {
            return;
        }

        _shots[computerId] = new Shot(data, contentType, DateTime.Now);

        // Καθάρισμα παλιών ώστε να μη μεγαλώνει η μνήμη.
        if (_shots.Count > 600)
        {
            foreach (var old in _shots.Where(kv => DateTime.Now - kv.Value.At > TimeSpan.FromMinutes(5)).ToList())
            {
                _shots.TryRemove(old.Key, out _);
            }
        }
    }

    public Shot? Get(int computerId)
    {
        if (_shots.TryGetValue(computerId, out var shot) && DateTime.Now - shot.At <= MaxAge)
        {
            return shot;
        }

        return null;
    }

    public bool Has(int computerId) => Get(computerId) != null;

    /// <summary>Κάποιος βλέπει τώρα τον τοίχο — οι agents να τραβούν οθόνη.</summary>
    public void MarkWatched()
    {
        Interlocked.Exchange(ref _watchedUntilTicks, (DateTime.Now + WatchWindow).Ticks);
    }

    public bool IsWatched => DateTime.Now.Ticks < Interlocked.Read(ref _watchedUntilTicks);
}
