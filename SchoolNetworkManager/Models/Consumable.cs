using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

/// <summary>Αναλώσιμο σε απόθεμα (toner, μελάνι, ποντίκια, καλώδια…).</summary>
public class Consumable
{
    public static readonly string[] Categories =
    {
        "Toner / Μελάνι", "Χαρτί", "Περιφερειακά", "Καλώδια", "Ανταλλακτικά", "Αποθήκευση", "Άλλο"
    };

    public int Id { get; set; }

    [Required(ErrorMessage = "Γράψε όνομα.")]
    [MaxLength(200)]
    [Display(Name = "Όνομα")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Κατηγορία")]
    public string Category { get; set; } = "Άλλο";

    [MaxLength(300)]
    [Display(Name = "Για (συμβατότητα)")]
    public string? CompatibleWith { get; set; }

    [Display(Name = "Απόθεμα")]
    public int Quantity { get; set; }

    [Display(Name = "Ελάχιστο")]
    public int MinQuantity { get; set; } = 1;

    [MaxLength(200)]
    [Display(Name = "Πού φυλάσσεται")]
    public string? StoredAt { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Σημειώσεις")]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    public bool IsLow => Quantity <= MinQuantity;
    public bool IsOut => Quantity <= 0;
}

/// <summary>Κίνηση αποθέματος (+ παραλαβή, − χρήση).</summary>
public class ConsumableLog
{
    public int Id { get; set; }
    public int ConsumableId { get; set; }
    public int Change { get; set; }
    public int QuantityAfter { get; set; }
    public string? Note { get; set; }
    public string? By { get; set; }
    public DateTime At { get; set; } = DateTime.Now;
}
