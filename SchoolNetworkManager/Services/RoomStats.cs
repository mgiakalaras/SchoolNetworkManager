using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Προγραμματισμένος έναντι πραγματικού αριθμού συσκευών ενός τύπου.</summary>
public record TypeCount(string MachineType, int Planned, int Actual)
{
    /// <summary>Ο πραγματικός αριθμός ισούται με τον προγραμματισμένο.</summary>
    public bool Ok => Planned == Actual;

    /// <summary>Υπάρχουν συσκευές τύπου που δεν έχει προγραμματιστεί.</summary>
    public bool Unplanned => Planned == 0 && Actual > 0;
}

/// <summary>Καθαρός υπολογισμός προγραμματισμένων/πραγματικών συσκευών ανά χώρο (χωρίς DbContext).</summary>
public static class RoomStats
{
    /// <summary>
    /// Μία εγγραφή ανά τύπο στην ένωση πλάνου και πραγματικών συσκευών, με σειρά της MachineTypes.All
    /// (άγνωστοι τύποι στο τέλος). Τα Dynamic και τα κενά slots (EMPTY) δεν μετράνε.
    /// </summary>
    public static IReadOnlyList<TypeCount> ForRoom(IEnumerable<Computer> devices, IEnumerable<RoomCapacity> plan)
    {
        var planned = plan
            .GroupBy(p => p.MachineType)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.PlannedCount));
        var actual = devices
            .Where(d => !d.IsEmptySlot)
            .GroupBy(d => d.DisplayMachineType)
            .ToDictionary(g => g.Key, g => g.Count());

        return Build(planned.Keys.Union(actual.Keys)
            .Select(t => new TypeCount(t, planned.GetValueOrDefault(t), actual.GetValueOrDefault(t))));
    }

    /// <summary>Σύνολα ορόφου/κτιρίου: άθροισμα Planned και Actual ανά τύπο.</summary>
    public static IReadOnlyList<TypeCount> Sum(IEnumerable<IReadOnlyList<TypeCount>> rooms) =>
        Build(rooms.SelectMany(r => r)
            .GroupBy(t => t.MachineType)
            .Select(g => new TypeCount(g.Key, g.Sum(t => t.Planned), g.Sum(t => t.Actual))));

    private static IReadOnlyList<TypeCount> Build(IEnumerable<TypeCount> counts) =>
        counts
            .Where(c => c.Planned != 0 || c.Actual != 0)
            .OrderBy(c => { var i = MachineTypes.All.IndexOf(c.MachineType); return i < 0 ? int.MaxValue : i; })
            .ThenBy(c => c.MachineType, StringComparer.Ordinal)
            .ToList();
}
