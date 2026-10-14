using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Passwords;

public class IndexModel : PageModel
{
    private readonly AppDbContext _context;

    public IndexModel(AppDbContext context)
    {
        _context = context;
    }

    // ---------- Φόρμα ----------

    [BindProperty] public string? Account { get; set; }
    [BindProperty] public string? WindowsUser { get; set; }
    [BindProperty] public string? Password { get; set; }
    [BindProperty] public string TargetMode { get; set; } = "devices";
    [BindProperty] public List<int> DeviceIds { get; set; } = new();
    [BindProperty] public string? OtherTarget { get; set; }
    [BindProperty] public bool UniquePerDevice { get; set; }
    [BindProperty] public bool UpdateDeviceCredentials { get; set; }
    [BindProperty] public string? SetBy { get; set; }
    [BindProperty] public string? Note { get; set; }
    [BindProperty] public PasswordOptions Options { get; set; } = new();

    // ---------- Προβολή ----------

    [BindProperty(SupportsGet = true)] public string? Batch { get; set; }
    [BindProperty(SupportsGet = true)] public int? Device { get; set; }

    public List<IGrouping<string, Computer>> DevicesByLocation { get; set; } = new();
    public Dictionary<int, Computer> DevicesById { get; set; } = new();
    public List<BatchView> History { get; set; } = new();
    public BatchView? JustSaved { get; set; }

    [TempData] public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();

        if (!string.IsNullOrWhiteSpace(Batch))
        {
            JustSaved = History.FirstOrDefault(b => b.BatchId == Batch);
        }

        if (Device.HasValue && DeviceIds.Count == 0)
        {
            DeviceIds.Add(Device.Value);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var account = (Account ?? string.Empty).Trim();

        if (account.Length == 0)
        {
            StatusMessage = "⚠️ Διάλεξε ή γράψε λογαριασμό (π.χ. Admin).";
            return RedirectToPage();
        }

        SetBy = UserPasswords.ActorName(User, SetBy);

        if (string.IsNullOrWhiteSpace(SetBy))
        {
            StatusMessage = "⚠️ Γράψε το όνομά σου στο «Από», για να ξέρουμε ποιος έβαλε τον κωδικό.";
            return RedirectToPage();
        }

        var batch = Guid.NewGuid().ToString("N")[..12];
        var now = DateTime.Now;
        var records = new List<PasswordRecord>();

        if (TargetMode == "other")
        {
            if (string.IsNullOrWhiteSpace(OtherTarget) || string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "⚠️ Γράψε πού μπαίνει ο κωδικός και τον κωδικό.";
                return RedirectToPage();
            }

            records.Add(NewRecord(account, null, OtherTarget.Trim(), Password, batch, now));
        }
        else
        {
            var ids = DeviceIds.Distinct().ToList();
            var devices = await _context.Computers.Where(c => ids.Contains(c.Id)).ToListAsync();

            if (devices.Count == 0)
            {
                StatusMessage = "⚠️ Διάλεξε τουλάχιστον μία συσκευή.";
                return RedirectToPage();
            }

            if (!UniquePerDevice && string.IsNullOrWhiteSpace(Password))
            {
                StatusMessage = "⚠️ Δεν υπάρχει κωδικός. Πάτα 🔄 στη γεννήτρια ή γράψε έναν.";
                return RedirectToPage();
            }

            foreach (var device in devices.OrderBy(d => NetworkTools.IpToNumber(d.IpAddress)))
            {
                var secret = UniquePerDevice ? PasswordGenerator.Generate(Options) : Password!;
                records.Add(NewRecord(account, device.Id, null, secret, batch, now));

                if (UpdateDeviceCredentials)
                {
                    if (!string.IsNullOrWhiteSpace(WindowsUser))
                    {
                        device.Username = WindowsUser.Trim();
                    }

                    device.Password = secret;
                    device.UpdatedAt = now;
                }
            }
        }

        _context.PasswordRecords.AddRange(records);
        await _context.SaveChangesAsync();

        StatusMessage = records.Count == 1
            ? "🔐 Ο κωδικός καταχωρήθηκε στο ιστορικό."
            : $"🔐 Καταχωρήθηκαν {records.Count} κωδικοί στο ιστορικό.";

        return RedirectToPage(new { Batch = batch });
    }

    private PasswordRecord NewRecord(string account, int? computerId, string? target, string secret, string batch, DateTime now) => new()
    {
        Account = account,
        ComputerId = computerId,
        Target = target,
        Secret = secret,
        SetBy = SetBy?.Trim(),
        SetAt = now,
        Note = string.IsNullOrWhiteSpace(Note) ? null : Note.Trim(),
        BatchId = batch
    };

    private async Task LoadAsync()
    {
        var computers = await _context.Computers.AsNoTracking().ToListAsync();

        DevicesById = computers.ToDictionary(c => c.Id);

        DevicesByLocation = computers
            .Where(c => !c.IsEmptySlot)
            .OrderBy(c => string.IsNullOrWhiteSpace(c.Location) ? "~" : c.Location)
            .ThenBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .GroupBy(c => string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location.Trim())
            .ToList();

        var records = await _context.PasswordRecords
            .OrderByDescending(r => r.SetAt)
            .Take(400)
            .ToListAsync();

        // Ποιος είναι ο «τρέχων» κωδικός για κάθε λογαριασμό + στόχο
        var current = records
            .GroupBy(r => (r.Account, r.ComputerId, r.Target))
            .Select(g => g.OrderByDescending(r => r.SetAt).First().Id)
            .ToHashSet();

        History = records
            .GroupBy(r => r.BatchId ?? $"single-{r.Id}")
            .Select(g => new BatchView
            {
                BatchId = g.Key,
                Account = g.First().Account,
                SetBy = g.First().SetBy,
                SetAt = g.Max(r => r.SetAt),
                Note = g.First().Note,
                Items = g.Select(r => new RecordView
                {
                    Record = r,
                    Device = r.ComputerId.HasValue ? DevicesById.GetValueOrDefault(r.ComputerId.Value) : null,
                    IsCurrent = current.Contains(r.Id)
                })
                .OrderBy(v => NetworkTools.IpToNumber(v.Device?.IpAddress))
                .ToList()
            })
            .OrderByDescending(b => b.SetAt)
            .ToList();
    }

    public class RecordView
    {
        public PasswordRecord Record { get; set; } = new();
        public Computer? Device { get; set; }
        public bool IsCurrent { get; set; }

        public string TargetLabel => Device != null
            ? (string.IsNullOrWhiteSpace(Device.Name) ? Device.IpAddress : Device.Name)
            : Record.ComputerId.HasValue ? "(διαγραμμένη συσκευή)" : Record.Target ?? "—";
    }

    public class BatchView
    {
        public string BatchId { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
        public string? SetBy { get; set; }
        public DateTime SetAt { get; set; }
        public string? Note { get; set; }
        public List<RecordView> Items { get; set; } = new();

        public bool SamePasswordForAll => Items.Select(i => i.Record.Secret).Distinct().Count() <= 1;
    }
}
