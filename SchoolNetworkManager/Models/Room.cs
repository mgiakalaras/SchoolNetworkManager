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
}
