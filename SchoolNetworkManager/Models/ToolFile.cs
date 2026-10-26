namespace SchoolNetworkManager.Models;

/// <summary>Αρχείο ή σύνδεσμος στη «Βιβλιοθήκη εργαλείων» (utilities, εγκαταστάτες, scripts, drivers…).</summary>
public class ToolFile
{
    public static readonly string[] Categories =
    {
        "🛠️ Εργαλεία συστήματος",
        "🌐 Δίκτυο",
        "💿 Εγκαταστάτες προγραμμάτων",
        "🎓 Εκπαιδευτικό λογισμικό",
        "📜 Scripts",
        "🖨️ Drivers",
        "📄 Οδηγίες & έγγραφα",
        "📦 Άλλο"
    };

    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Category { get; set; } = "📦 Άλλο";

    public string? Version { get; set; }

    /// <summary>Αν είναι σύνδεσμος αντί για αρχείο (π.χ. σελίδα λήψης του κατασκευαστή).</summary>
    public string? Url { get; set; }

    public string? FileName { get; set; }

    public string? StoredName { get; set; }

    public long Size { get; set; }

    public string? Sha256 { get; set; }

    public string? UploadedBy { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }

    public int Downloads { get; set; }

    /// <summary>Φαίνεται και στη δημόσια σελίδα (για μαθητές / εκπαιδευτικούς).</summary>
    public bool IsPublic { get; set; }

    public bool IsLink => !string.IsNullOrWhiteSpace(Url) && string.IsNullOrWhiteSpace(StoredName);

    public string Icon
    {
        get
        {
            if (IsLink) return "🔗";
            var ext = Path.GetExtension(FileName ?? string.Empty).ToLowerInvariant();
            return ext switch
            {
                ".exe" or ".msi" or ".msix" => "💿",
                ".zip" or ".7z" or ".rar" => "🗜️",
                ".iso" or ".img" => "📀",
                ".ps1" or ".bat" or ".cmd" or ".sh" or ".py" or ".reg" => "📜",
                ".pdf" => "📕",
                ".doc" or ".docx" or ".odt" or ".txt" or ".md" => "📄",
                ".xlsx" or ".csv" => "📊",
                _ => "📦"
            };
        }
    }
}
