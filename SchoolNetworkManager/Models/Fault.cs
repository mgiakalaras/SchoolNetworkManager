using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

/// <summary>Βιβλίο βλαβών: μία αναφορά προβλήματος για μια συσκευή.</summary>
public class Fault
{
    public const string SeverityMinor = "Λειτουργεί με πρόβλημα";
    public const string SeverityDown = "Εκτός λειτουργίας";

    public static readonly string[] Severities = { SeverityMinor, SeverityDown };

    public int Id { get; set; }

    public int ComputerId { get; set; }

    [Required(ErrorMessage = "Γράψε τι πρόβλημα έχει.")]
    [MaxLength(1000)]
    [Display(Name = "Περιγραφή")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Σοβαρότητα")]
    public string Severity { get; set; } = SeverityMinor;

    [MaxLength(100)]
    [Display(Name = "Αναφέρθηκε από")]
    public string? ReportedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ResolvedAt { get; set; }

    /// <summary>Ποιος χρήστης έκλεισε τη βλάβη.</summary>
    [MaxLength(100)]
    public string? ResolvedBy { get; set; }

    /// <summary>Ποιος χρήστης την καταχώρησε (το «Αναφέρθηκε από» μπορεί να είναι άλλος συνάδελφος).</summary>
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Τι έγινε")]
    public string? Resolution { get; set; }

    public bool IsOpen => ResolvedAt == null;

    public bool IsDown => Severity == SeverityDown;
}
