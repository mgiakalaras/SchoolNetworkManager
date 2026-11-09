using System.Text.Json;

namespace SchoolNetworkManager.Models;

/// <summary>
/// Τι έχει κλειδώσει το LabLock σε έναν υπολογιστή: η τελευταία αναφορά του (μία γραμμή ανά συσκευή).
/// Το LabLock τη στέλνει μετά από κάθε Εφαρμογή, Αναίρεση ή Επαναφορά.
/// </summary>
public class LabLockStatus
{
    public int Id { get; set; }

    public int ComputerId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public string? LabLockVersion { get; set; }

    /// <summary>apply · undo · restore · board · check</summary>
    public string? Event { get; set; }

    public string? Hostname { get; set; }

    /// <summary>Ο λογαριασμός μαθητή που κλειδώθηκε.</summary>
    public string? Student { get; set; }

    /// <summary>JSON: [{id, group, title, state, kind, recorded}]</summary>
    public string? ItemsJson { get; set; }

    public int AppliedCount { get; set; }

    public int TotalCount { get; set; }

    public List<LabLockItem> ReadItems()
    {
        if (string.IsNullOrWhiteSpace(ItemsJson))
        {
            return new List<LabLockItem>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<LabLockItem>>(ItemsJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<LabLockItem>();
        }
        catch (JsonException)
        {
            return new List<LabLockItem>();
        }
    }

    public static string EventLabel(string? kind) => kind switch
    {
        "apply" => "🔒 Εφαρμογή",
        "undo" => "↩️ Αναίρεση",
        "restore" => "♻️ Επαναφορά PC",
        "board" => "🖥️ Πίνακας",
        "check" => "🔗 Σύνδεση",
        _ => kind ?? "—"
    };
}

/// <summary>
/// Μία αναφορά του LabLock στο ιστορικό ενός υπολογιστή (ποιος/τι/πότε), χωρίς τις ρυθμίσεις μία-μία.
/// Κρατιούνται οι τελευταίες <see cref="Services.LabLockIngest.HistoryKeep"/> ανά υπολογιστή.
/// </summary>
public class LabLockEvent
{
    public int Id { get; set; }

    public int ComputerId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    /// <summary>apply · undo · restore · board</summary>
    public string? Event { get; set; }

    public string? Student { get; set; }

    public string? LabLockVersion { get; set; }

    public int AppliedCount { get; set; }

    public int TotalCount { get; set; }
}

/// <summary>Μία ρύθμιση του LabLock και η κατάστασή της στον υπολογιστή.</summary>
public class LabLockItem
{
    public string? Id { get; set; }

    public string? Group { get; set; }

    public string? Title { get; set; }

    public string? State { get; set; }

    /// <summary>applied · partly · off · unknown</summary>
    public string? Kind { get; set; }

    /// <summary>Πότε την εφάρμοσε το LabLock (από το αρχείο αλλαγών του υπολογιστή), αλλιώς κενό.</summary>
    public string? Recorded { get; set; }
}
