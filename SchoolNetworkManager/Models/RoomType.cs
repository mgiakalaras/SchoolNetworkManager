namespace SchoolNetworkManager.Models;

/// <summary>Είδος χώρου (π.χ. Εργαστήριο Πληροφορικής, Γραφείο).</summary>
public class RoomType
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Icon { get; set; }

    public string? Color { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Προγραμματισμένος αριθμός συσκευών ενός τύπου (MachineTypes) σε χώρο.</summary>
public class RoomCapacity
{
    public int Id { get; set; }

    public int RoomId { get; set; }

    public string MachineType { get; set; } = string.Empty;

    public int PlannedCount { get; set; }
}

/// <summary>Οι προεπιλεγμένοι τύποι χώρων, με τη σειρά εμφάνισης.</summary>
public static class RoomTypeDefaults
{
    public static readonly IReadOnlyList<(string Name, string Icon)> All = new List<(string Name, string Icon)>
    {
        ("Εργαστήριο Πληροφορικής", "💻"),
        ("Αίθουσα διδασκαλίας", "🏫"),
        ("Εργαστήριο ειδικότητας", "🛠️"),
        ("Γραφείο", "🗂️"),
        ("Αίθουσα καθηγητών", "☕"),
        ("Βιβλιοθήκη", "📚"),
        ("Αποθήκη", "📦"),
        ("Server / Rack", "🗄️"),
        ("Άλλο", "▫️"),
    };
}
