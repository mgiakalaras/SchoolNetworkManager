using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>Διαχείριση της λίστας τύπων χώρων (προσθήκη, επεξεργασία, σειρά, διαγραφή).</summary>
public static class RoomTypeOps
{
    public const int MaxNameLength = 80;
    public const int MaxIconLength = 8;

    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$", RegexOptions.Compiled);

    /// <summary>Το χρώμα επιτρέπεται να μπει σε style μόνο αν είναι #rgb ή #rrggbb.</summary>
    public static bool IsValidColor(string? color) => color != null && HexColor.IsMatch(color);

    /// <summary>Προσθέτει (id = null) ή ενημερώνει τύπο. Οι χώροι που τον χρησιμοποιούν μένουν συνδεδεμένοι (σύνδεση με Id).</summary>
    public static async Task SaveAsync(AppDbContext db, int? id, string name, string? icon, string? color)
    {
        var cleanName = (name ?? string.Empty).Trim();
        var cleanIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        var cleanColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();

        if (cleanName.Length == 0)
        {
            throw new InvalidOperationException("Το όνομα του τύπου είναι υποχρεωτικό.");
        }

        if (cleanName.Length > MaxNameLength)
        {
            throw new InvalidOperationException($"Το όνομα δεν μπορεί να ξεπερνά τους {MaxNameLength} χαρακτήρες.");
        }

        if (cleanIcon != null && cleanIcon.Length > MaxIconLength)
        {
            throw new InvalidOperationException($"Το εικονίδιο δεν μπορεί να ξεπερνά τους {MaxIconLength} χαρακτήρες.");
        }

        if (cleanColor != null && !IsValidColor(cleanColor))
        {
            throw new InvalidOperationException("Μη έγκυρο χρώμα. Χρησιμοποίησε μορφή #rrggbb.");
        }

        var all = await db.RoomTypes.ToListAsync();

        if (all.Any(t => t.Id != id && RoomService.SameName(t.Name, cleanName)))
        {
            throw new InvalidOperationException($"Υπάρχει ήδη τύπος με όνομα «{cleanName}».");
        }

        RoomType? type = null;

        if (id != null)
        {
            type = all.FirstOrDefault(t => t.Id == id)
                ?? throw new InvalidOperationException("Ο τύπος δεν βρέθηκε.");
        }
        else
        {
            type = new RoomType { SortOrder = all.Count == 0 ? 0 : all.Max(t => t.SortOrder) + 1 };
            db.RoomTypes.Add(type);
        }

        type.Name = cleanName;
        type.Icon = cleanIcon;
        type.Color = cleanColor;

        await db.SaveChangesAsync();
    }

    /// <summary>Διαγράφει τύπο· αρνείται αν τον χρησιμοποιεί έστω ένας χώρος.</summary>
    public static async Task DeleteAsync(AppDbContext db, int id)
    {
        var type = await db.RoomTypes.FirstOrDefaultAsync(t => t.Id == id);

        if (type == null)
        {
            return;
        }

        var used = await db.Rooms.CountAsync(r => r.RoomTypeId == id);

        if (used > 0)
        {
            throw new InvalidOperationException(
                $"Ο τύπος «{type.Name}» χρησιμοποιείται από {used} χώρο(υς) και δεν μπορεί να διαγραφεί.");
        }

        db.RoomTypes.Remove(type);
        await db.SaveChangesAsync();
    }

    /// <summary>Μετακινεί τον τύπο μία θέση πάνω (dir &lt; 0) ή κάτω (dir &gt; 0).</summary>
    public static async Task MoveAsync(AppDbContext db, int id, int dir)
    {
        var ordered = await db.RoomTypes.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToListAsync();

        // Κανονικοποίηση ώστε να μη χαλάνε οι ισοβαθμίες το swap
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
        }

        var index = ordered.FindIndex(t => t.Id == id);
        var target = index + (dir < 0 ? -1 : 1);

        if (index >= 0 && target >= 0 && target < ordered.Count)
        {
            (ordered[index].SortOrder, ordered[target].SortOrder) = (ordered[target].SortOrder, ordered[index].SortOrder);
        }

        await db.SaveChangesAsync();
    }
}
