namespace SchoolNetworkManager.Models;

/// <summary>
/// Εντολή προς τον agent ενός υπολογιστή (κλείσιμο, επανεκκίνηση, μήνυμα…).
/// Ο agent ρωτάει τον server κάθε λίγα δευτερόλεπτα και την εκτελεί ως SYSTEM.
/// </summary>
public class AgentCommand
{
    public const int Pending = 0;
    public const int Sent = 1;
    public const int Done = 2;
    public const int Failed = 3;
    public const int Expired = 4;
    public const int Cancelled = 5;

    public int Id { get; set; }

    public int ComputerId { get; set; }

    /// <summary>shutdown · restart · logoff · cancel · message · lock · unlock</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Μήνυμα που βλέπει ο χρήστης του υπολογιστή.</summary>
    public string? Text { get; set; }

    /// <summary>Καθυστέρηση πριν το κλείσιμο / την επανεκκίνηση (δευτερόλεπτα).</summary>
    public int DelaySeconds { get; set; }

    public int Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? DoneAt { get; set; }

    public string? Result { get; set; }

    /// <summary>Δημιουργήθηκε από το αυτόματο βραδινό κλείσιμο.</summary>
    public bool IsAutomatic { get; set; }

    public static string Label(string kind) => kind switch
    {
        "shutdown" => "⏻ Κλείσιμο",
        "restart" => "🔄 Επανεκκίνηση",
        "logoff" => "🚪 Αποσύνδεση χρήστη",
        "cancel" => "✋ Ακύρωση κλεισίματος",
        "message" => "💬 Μήνυμα",
        "lock" => "🔒 Κλείδωμα οθόνης",
        "unlock" => "🔓 Ξεκλείδωμα οθόνης",
        _ => kind
    };

    public static string StatusLabel(int status) => status switch
    {
        Pending => "⏳ σε αναμονή",
        Sent => "📨 στάλθηκε",
        Done => "✔ έγινε",
        Failed => "✖ απέτυχε",
        Expired => "⌛ έληξε (κλειστός;)",
        Cancelled => "✋ ακυρώθηκε",
        _ => "?"
    };
}
