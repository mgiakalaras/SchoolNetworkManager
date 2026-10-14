namespace SchoolNetworkManager.Models;

/// <summary>
/// Κείμενο του σχολείου που επεξεργάζεται η ομάδα (κανονισμός εργαστηρίων, κανόνες μαθητών).
/// Το κείμενο γράφεται απλά: «# Τίτλος», «- κουκκίδα», «1. αρίθμηση», κενή γραμμή = νέα παράγραφος.
/// Μπορεί να έχει και επίσημο αρχείο (Word / PDF) για λήψη.
/// </summary>
public class SchoolDocument
{
    public const string LabRules = "lab-rules";
    public const string StudentRules = "student-rules";

    public static readonly string[] Keys = { LabRules, StudentRules };

    public int Id { get; set; }

    public string Key { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Φαίνεται στη δημόσια σελίδα (χωρίς κωδικό).</summary>
    public bool IsPublic { get; set; } = true;

    public string? FilePath { get; set; }

    public string? FileName { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public string? UpdatedBy { get; set; }

    public string Icon => Key == StudentRules ? "🎓" : "📜";
}
