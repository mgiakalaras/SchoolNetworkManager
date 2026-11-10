using System.Text.Json;

namespace SchoolNetworkManager.Services;

/// <summary>Θέση χώρου πάνω στην κάτοψη ορόφου, σε ποσοστά (0–100) του καμβά.</summary>
public record RoomBox(int RoomId, double X, double Y, double W, double H);

/// <summary>Καθαρή λογική της κάτοψης ορόφου: έλεγχος της διάταξης που στέλνει ο browser και κατάσταση κάρτας.</summary>
public static class FloorPlanLayout
{
    private const int MaxBoxes = 500;

    private sealed class BoxDto
    {
        public int RoomId { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
    }

    /// <summary>
    /// Διαβάζει JSON <c>[{roomId,x,y,w,h}]</c>. Επιστρέφει null αν το JSON δεν είναι έγκυρο (τότε δεν αλλάζει τίποτα).
    /// Κρατά μόνο χώρους του ορόφου (πρώτη εμφάνιση κάθε χώρου), περιορίζει τις τιμές στο 0–100
    /// και φροντίζει να ισχύει x+w ≤ 100 και y+h ≤ 100.
    /// </summary>
    public static List<RoomBox>? Sanitise(string? json, IReadOnlyCollection<int> floorRoomIds)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        List<BoxDto?>? dtos;

        try
        {
            dtos = JsonSerializer.Deserialize<List<BoxDto?>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }

        if (dtos == null)
        {
            return null;
        }

        var allowed = floorRoomIds.ToHashSet();
        var seen = new HashSet<int>();
        var result = new List<RoomBox>();

        foreach (var d in dtos.Take(MaxBoxes))
        {
            if (d == null || !allowed.Contains(d.RoomId) || !seen.Add(d.RoomId))
            {
                continue;
            }

            var x = Clamp(d.X);
            var y = Clamp(d.Y);
            var w = Math.Min(Clamp(d.W), 100 - x);
            var h = Math.Min(Clamp(d.H), 100 - y);

            result.Add(new RoomBox(d.RoomId, x, y, w, h));
        }

        return result;
    }

    private static double Clamp(double v) => Math.Round(Math.Clamp(double.IsFinite(v) ? v : 0, 0, 100), 2);

    /// <summary>"none" = ούτε συσκευές ούτε πλάνο, "ok" = όλοι οι τύποι σωστοί και κανένας εκτός πλάνου, αλλιώς "warn".</summary>
    public static string Status(IReadOnlyList<TypeCount> counts)
    {
        if (counts.Count == 0)
        {
            return "none";
        }

        return counts.All(c => c.Ok) && !counts.Any(c => c.Unplanned) ? "ok" : "warn";
    }
}
