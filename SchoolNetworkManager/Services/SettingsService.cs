using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Κρατά τις ρυθμίσεις στη μνήμη ώστε να μη διαβάζεται η βάση σε κάθε σελίδα.
/// Ενημερώνεται κατά την εκκίνηση και κάθε φορά που αποθηκεύονται οι ρυθμίσεις.
/// </summary>
public sealed class SettingsService
{
    private volatile AppSettings _current = new();

    public AppSettings Current => _current;

    public void Update(AppSettings settings)
    {
        _current = settings.Clone();
    }
}
