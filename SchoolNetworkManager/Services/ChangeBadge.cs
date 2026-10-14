using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

public enum ChangeKind
{
    None,
    New,
    Updated
}

/// <summary>
/// Ενδείξεις «ΝΕΟ» (πρόσφατη καταχώρηση) και «ΕΝΗΜ.» (πρόσφατα συμπληρώθηκαν στοιχεία).
/// Ισχύουν για Χ ημέρες (ρύθμιση) ή μέχρι να πατηθεί «Όλα ειδωμένα».
/// </summary>
public static class ChangeBadge
{
    public static DateTime Since(AppSettings settings)
    {
        var days = Math.Clamp(settings.NewBadgeDays, 0, 365);
        var since = DateTime.Now.AddDays(-days);

        if (settings.BadgesClearedAt != null && settings.BadgesClearedAt > since)
        {
            since = settings.BadgesClearedAt.Value;
        }

        return since;
    }

    public static ChangeKind For(Computer computer, AppSettings settings)
    {
        if (settings.NewBadgeDays <= 0)
        {
            return ChangeKind.None;
        }

        var since = Since(settings);

        if (computer.CreatedAt > since)
        {
            return ChangeKind.New;
        }

        if (computer.UpdatedAt != null && computer.UpdatedAt > since)
        {
            return ChangeKind.Updated;
        }

        return ChangeKind.None;
    }

    /// <summary>Η πιο πρόσφατη αλλαγή (καταχώρηση ή ενημέρωση) — για ταξινόμηση «Πιο πρόσφατα».</summary>
    public static DateTime LastChange(Computer computer) =>
        computer.UpdatedAt != null && computer.UpdatedAt > computer.CreatedAt ? computer.UpdatedAt.Value : computer.CreatedAt;
}
