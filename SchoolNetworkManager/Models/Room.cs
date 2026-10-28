namespace SchoolNetworkManager.Models;

/// <summary>
/// Χώρος που δημιουργήθηκε από τη σελίδα «Χώροι» (μπορεί να είναι ακόμη άδειος).
/// Οι συσκευές συνδέονται με το όνομα (Computer.Location), όπως και πριν.
/// </summary>
public class Room
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Ώρα αυτόματου κλεισίματος των υπολογιστών του χώρου («21:00»· κενό = όχι).</summary>
    public string? AutoShutdown { get; set; }

    /// <summary>Πότε έγινε τελευταία φορά το αυτόματο κλείσιμο (για να γίνεται μία φορά τη μέρα).</summary>
    public DateTime? AutoShutdownLast { get; set; }
}
