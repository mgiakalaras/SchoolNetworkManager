using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Παραλαβή αναφορών από τον agent των υπολογιστών (snm-agent.ps1):
/// βρίσκει τη συσκευή (MAC → IP → όνομα) ή τη δημιουργεί, ενημερώνει χαρακτηριστικά,
/// Veyon, δίσκους και τη λίστα εγκατεστημένων προγραμμάτων.
/// </summary>
public static class AgentIngest
{
    /// <summary>Η έκδοση του snm-agent.ps1 που διανέμει ο server (οι agents ενημερώνονται μόνοι τους).</summary>
    public const string LatestAgentVersion = "1.2.0";

    public const string AgentUser = "agent";

    // ---------------- Τι στέλνει ο agent ----------------

    public class Payload
    {
        public string? AgentVersion { get; set; }
        public string? Hostname { get; set; }
        public string? Domain { get; set; }
        public string? User { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? Serial { get; set; }
        public string? Chassis { get; set; }
        public OsInfo? Os { get; set; }
        public CpuInfo? Cpu { get; set; }
        public double? RamGb { get; set; }
        public List<string>? Gpu { get; set; }
        public List<DiskInfo>? Disks { get; set; }
        public List<PhysicalDiskInfo>? PhysicalDisks { get; set; }
        public List<NetInfo>? Network { get; set; }
        public VeyonInfo? Veyon { get; set; }
        public List<SoftwareInfo>? Software { get; set; }
        public string? RustDeskId { get; set; }
    }

    public class OsInfo
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Build { get; set; }
        public string? DisplayVersion { get; set; }
        public string? Arch { get; set; }
        public string? LastBoot { get; set; }
    }

    public class CpuInfo
    {
        public string? Name { get; set; }
        public int? Cores { get; set; }
        public int? Threads { get; set; }
    }

    public class DiskInfo
    {
        public string? Drive { get; set; }
        public string? Label { get; set; }
        public double SizeGb { get; set; }
        public double FreeGb { get; set; }

        [JsonIgnore]
        public double FreePercent => SizeGb <= 0 ? 100 : Math.Round(FreeGb * 100 / SizeGb, 1);
    }

    public class PhysicalDiskInfo
    {
        public string? Model { get; set; }
        public string? MediaType { get; set; }
        public double SizeGb { get; set; }
    }

    public class NetInfo
    {
        public string? Description { get; set; }
        public bool? Virtual { get; set; }
        public string? Mac { get; set; }

        /// <summary>VPN / ZeroTier / VirtualBox κ.λπ. — δεν είναι η κάρτα δικτύου του υπολογιστή.</summary>
        [JsonIgnore]
        public bool IsVirtual => Virtual == true || System.Text.RegularExpressions.Regex.IsMatch(Description ?? string.Empty,
            "Virtual|ZeroTier|VPN|TAP-|Hyper-V|VMware|VirtualBox|Tailscale|WireGuard|Loopback|Bluetooth|WAN Miniport|Npcap",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public List<string>? Ips { get; set; }
        public string? Gateway { get; set; }
        public bool? Dhcp { get; set; }
    }

    public class VeyonInfo
    {
        public bool Installed { get; set; }
        public string? Version { get; set; }
        public string? Service { get; set; }
    }

    public class SoftwareInfo
    {
        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? Publisher { get; set; }
        public string? InstallDate { get; set; }
        public string? Scope { get; set; }
    }

    public record Result(bool Ok, string Message, int? ComputerId = null, bool Created = false);

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // ---------------- Κλειδί ----------------

    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(18)).ToLowerInvariant();

    public static bool KeyMatches(string? expected, string? given)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(given))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)),
            SHA256.HashData(Encoding.UTF8.GetBytes(given.Trim())));
    }

    // Ένας agent στέλνει το πολύ μία αναφορά ανά 20″ (προστασία από λάθος ρύθμιση / κατάχρηση).
    private static readonly ConcurrentDictionary<string, DateTime> LastHit = new();

    public static bool Throttle(string? client)
    {
        var key = client ?? "?";
        var now = DateTime.UtcNow;

        if (LastHit.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(20))
        {
            return true;
        }

        LastHit[key] = now;

        if (LastHit.Count > 5000)
        {
            LastHit.Clear();
        }

        return false;
    }

    // ---------------- Επεξεργασία ----------------

    public static async Task<Result> ProcessAsync(AppDbContext db, Payload data, string? remoteIp)
    {
        var hostname = Clean(data.Hostname, 64);

        if (string.IsNullOrWhiteSpace(hostname))
        {
            return new Result(false, "Λείπει το όνομα υπολογιστή.");
        }

        var now = DateTime.Now;
        // Πρώτα οι πραγματικές κάρτες δικτύου, μετά τα VPN / εικονικά
        var adapters = (data.Network ?? new List<NetInfo>())
            .Where(n => NetworkTools.IsUsableMac(NetworkTools.NormalizeMac(n.Mac)))
            .OrderBy(n => n.IsVirtual)
            .ToList();

        var macs = adapters
            .Select(n => NetworkTools.NormalizeMac(n.Mac)!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ips = adapters
            .SelectMany(n => n.Ips ?? new List<string>())
            .Where(ip => IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !ip.StartsWith("169.254.") && !ip.StartsWith("127."))
            .Distinct()
            .ToList();

        // Η IP από την οποία ήρθε η αναφορά είναι η πιο αξιόπιστη (αυτή βλέπει και ο server).
        if (!string.IsNullOrWhiteSpace(remoteIp) && ips.Contains(remoteIp))
        {
            ips.Remove(remoteIp);
            ips.Insert(0, remoteIp);
        }

        var all = await db.Computers.ToListAsync();
        var warnings = new List<string>();
        var created = false;

        // 1) MAC  2) IP  3) όνομα
        var computer = all.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.MacAddress) &&
                                               macs.Contains(NetworkTools.NormalizeMac(c.MacAddress)!, StringComparer.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(c => ips.Contains(c.IpAddress.Trim()))
                       ?? all.FirstOrDefault(c => !c.IsDynamic && string.Equals(c.Name?.Trim(), hostname, StringComparison.OrdinalIgnoreCase));

        var primaryIp = ips.FirstOrDefault();
        var primaryAdapter = adapters.FirstOrDefault(a => (a.Ips ?? new()).Contains(primaryIp ?? "-"));

        // Αν η αναφορά ήρθε μέσω VPN, η MAC της συσκευής είναι της πραγματικής κάρτας.
        if (primaryAdapter == null || primaryAdapter.IsVirtual)
        {
            primaryAdapter = adapters.FirstOrDefault(a => !a.IsVirtual) ?? primaryAdapter;
        }

        var primaryMac = primaryAdapter?.Mac is { } m ? NetworkTools.NormalizeMac(m) : macs.FirstOrDefault();

        if (computer == null)
        {
            if (primaryIp == null)
            {
                return new Result(false, "Δεν βρέθηκε IPv4 διεύθυνση στον υπολογιστή.");
            }

            computer = new Computer
            {
                IpAddress = primaryIp,
                Name = hostname,
                MacAddress = primaryMac,
                MachineType = string.Equals(data.Chassis, "laptop", StringComparison.OrdinalIgnoreCase) ? "Laptop" : MachineTypes.Default,
                CreatedAt = now,
                UpdatedBy = AgentUser
            };

            db.Computers.Add(computer);
            created = true;
        }

        var changed = created;

        // Ο agent τρέχει σε σταθερό υπολογιστή του σχολείου → δεν είναι «dynamic».
        if (computer.IsDynamic)
        {
            computer.IsDynamic = false;
            changed = true;
        }

        // Όνομα: συμπληρώνεται αν είναι κενό ή αν ήταν το προηγούμενο όνομα του Windows.
        var previous = await db.AgentReports.FirstOrDefaultAsync(r => computer.Id > 0 && r.ComputerId == computer.Id);

        if (string.IsNullOrWhiteSpace(computer.Name) ||
            (previous?.Hostname != null && string.Equals(computer.Name, previous.Hostname, StringComparison.OrdinalIgnoreCase) && computer.Name != hostname))
        {
            computer.Name = hostname;
            changed = true;
        }

        // MAC
        if (string.IsNullOrWhiteSpace(computer.MacAddress) && primaryMac != null)
        {
            computer.MacAddress = primaryMac;
            changed = true;
        }
        else if (!string.IsNullOrWhiteSpace(computer.MacAddress) && !macs.Contains(NetworkTools.NormalizeMac(computer.MacAddress)!, StringComparer.OrdinalIgnoreCase))
        {
            computer.DetectedMac = primaryMac;   // φαίνεται ⚠️ «άλλη MAC» στη λίστα
        }
        else
        {
            computer.DetectedMac = null;
        }

        // IP: αν άλλαξε, μεταφέρεται — εκτός αν την έχει άλλη πραγματική συσκευή.
        if (!created && primaryIp != null && !ips.Contains(computer.IpAddress.Trim()))
        {
            var holder = all.FirstOrDefault(c => c.Id != computer.Id && c.IpAddress.Trim() == primaryIp);

            if (holder == null)
            {
                computer.IpAddress = primaryIp;
                changed = true;
            }
            else if (holder.IsEmptySlot || holder.IsDynamic)
            {
                // ανταλλαγή: η άδεια θέση παίρνει την παλιά IP
                var old = computer.IpAddress;
                holder.IpAddress = "0.0.0.0-swap-" + holder.Id;
                await db.SaveChangesAsync();
                computer.IpAddress = primaryIp;
                await db.SaveChangesAsync();
                holder.IpAddress = old;
                changed = true;
            }
            else
            {
                warnings.Add($"Ο υπολογιστής έχει πλέον IP {primaryIp}, που είναι καταχωρημένη στη συσκευή «{holder.Name ?? holder.IpAddress}».");
            }
        }

        // Χαρακτηριστικά
        var specs = DeviceSpecs.Parse(computer.Specs);
        var before = JsonSerializer.Serialize(specs);

        SetSpec(specs, "pc_cpu", data.Cpu?.Name);
        SetSpec(specs, "pc_cores", data.Cpu?.Cores is > 0 ? $"{data.Cpu.Cores} / {data.Cpu.Threads ?? data.Cpu.Cores}" : null);
        SetSpec(specs, "pc_ram", data.RamGb is > 0 ? $"{Math.Round(data.RamGb.Value):0} GB" : null);
        SetSpec(specs, "pc_storage", StorageText(data));
        SetSpec(specs, "pc_gpu", data.Gpu == null ? null : string.Join(" + ", data.Gpu.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct()));
        SetSpec(specs, "pc_os", OsText(data.Os));
        SetSpec(specs, "common_brand", string.Join(" ", new[] { data.Manufacturer, data.Model }.Where(x => !string.IsNullOrWhiteSpace(x) && !IsJunk(x))));
        SetSpec(specs, "common_serial", IsJunk(data.Serial) ? null : data.Serial);

        if (JsonSerializer.Serialize(specs) != before)
        {
            computer.Specs = specs.Count == 0 ? null : JsonSerializer.Serialize(specs);
            changed = true;
        }

        // Veyon
        var veyonInstalled = data.Veyon?.Installed == true;
        var veyonRunning = veyonInstalled && string.Equals(data.Veyon?.Service, "Running", StringComparison.OrdinalIgnoreCase);

        if (veyonInstalled && !computer.HasVeyon)
        {
            computer.HasVeyon = true;
            changed = true;
        }

        if (veyonInstalled && !veyonRunning)
        {
            if (!computer.VeyonDown)
            {
                computer.VeyonDown = true;
                computer.VeyonDownSince = now;
            }
        }
        else if (veyonRunning && computer.VeyonDown)
        {
            computer.VeyonDown = false;
            computer.VeyonDownSince = null;
        }

        computer.IsOnline = true;
        computer.LastSeenOnline = now;
        computer.LastCheckedAt = now;

        if (changed)
        {
            computer.UpdatedAt = now;
            computer.UpdatedBy = AgentUser;
        }

        await db.SaveChangesAsync();

        // Αναφορά
        var report = previous ?? new AgentReport { ComputerId = computer.Id, FirstReportAt = now };

        if (previous == null)
        {
            db.AgentReports.Add(report);
        }

        var disks = (data.Disks ?? new List<DiskInfo>()).Where(d => d.SizeGb > 1).ToList();

        report.ReceivedAt = now;
        report.AgentVersion = Clean(data.AgentVersion, 20);
        report.Hostname = hostname;
        report.LoggedUser = Clean(data.User, 100);
        report.OsName = Clean(OsText(data.Os), 120);
        report.OsVersion = Clean(data.Os?.Version, 40);
        report.LastBoot = ParseDate(data.Os?.LastBoot);
        report.CpuName = Clean(data.Cpu?.Name, 120);
        report.RamGb = data.RamGb;
        report.DisksJson = JsonSerializer.Serialize(disks.Select(d => new { d.Drive, d.Label, SizeGb = Math.Round(d.SizeGb, 1), FreeGb = Math.Round(d.FreeGb, 1) }));
        report.MinFreePercent = disks.Count == 0 ? null : disks.Min(d => d.FreePercent);
        report.Manufacturer = Clean(data.Manufacturer, 80);
        report.Model = Clean(data.Model, 80);
        report.Serial = IsJunk(data.Serial) ? null : Clean(data.Serial, 60);
        report.VeyonInstalled = veyonInstalled;
        report.VeyonRunning = veyonRunning;
        report.VeyonVersion = Clean(data.Veyon?.Version, 30);
        report.RustDeskId = Clean(data.RustDeskId, 32);
        report.IpList = string.Join(", ", ips);
        report.MacList = string.Join(", ", macs);
        report.Warning = warnings.Count == 0 ? null : string.Join(" ", warnings);

        // Λογισμικό: αντικατάσταση, κρατώντας πότε πρωτοεμφανίστηκε κάθε πρόγραμμα
        var software = (data.Software ?? new List<SoftwareInfo>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Name))
            .GroupBy(s => SoftwareKey(s.Name, s.Publisher))
            .Select(g => g.First())
            .Take(3000)
            .ToList();

        var existing = await db.InstalledSoftware.Where(s => s.ComputerId == computer.Id).ToListAsync();
        var firstSeen = existing
            .GroupBy(s => SoftwareKey(s.Name, s.Publisher))
            .ToDictionary(g => g.Key, g => g.Min(s => s.FirstSeen));

        db.InstalledSoftware.RemoveRange(existing);

        foreach (var s in software)
        {
            var (displayName, publisher) = s.Scope == "store" ? StoreNames(s.Name!, s.Publisher) : (s.Name!, s.Publisher);

            db.InstalledSoftware.Add(new InstalledSoftware
            {
                ComputerId = computer.Id,
                Name = Clean(displayName, 200)!,
                Version = Clean(s.Version, 60),
                Publisher = Clean(publisher, 120),
                InstallDate = Clean(s.InstallDate, 20),
                Scope = s.Scope is "user" or "store" ? s.Scope : "machine",
                FirstSeen = firstSeen.GetValueOrDefault(SoftwareKey(s.Name, s.Publisher), now)
            });
        }

        report.SoftwareCount = software.Count;
        await db.SaveChangesAsync();

        var message = created ? $"Νέα συσκευή «{hostname}» ({computer.IpAddress})." : $"Ενημερώθηκε «{computer.Name}» ({computer.IpAddress}).";

        return new Result(true, message + (warnings.Count > 0 ? " " + report.Warning : string.Empty), computer.Id, created);
    }

    /// <summary>
    /// Εφαρμογές Store χωρίς φιλικό όνομα: «5A894077.McAfeeSecurity» → «McAfee Security»,
    /// και ο εκδότης-hash (π.χ. «wafk5atnkzcwy») δεν εμφανίζεται.
    /// </summary>
    public static (string Name, string? Publisher) StoreNames(string name, string? publisher)
    {
        var display = name.Trim();

        if (!display.Contains(' ') && display.Contains('.'))
        {
            var parts = display.Split('.', StringSplitOptions.RemoveEmptyEntries);
            display = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : display;
            display = System.Text.RegularExpressions.Regex.Replace(display, "(?<=[a-z])(?=[A-Z])", " ");
        }

        var hashLike = publisher != null && System.Text.RegularExpressions.Regex.IsMatch(publisher, "^[a-z0-9]{10,16}$");

        return (display, hashLike ? null : publisher);
    }

    public static string SoftwareKey(string? name, string? publisher) =>
        $"{name?.Trim().ToLowerInvariant()}|{publisher?.Trim().ToLowerInvariant()}";

    /// <summary>«Νέο»: εμφανίστηκε μετά την πρώτη αναφορά του υπολογιστή (όχι στην αρχική καταγραφή).</summary>
    public static bool IsNew(InstalledSoftware software, AgentReport? report) =>
        report != null && software.FirstSeen > report.FirstReportAt.AddMinutes(30);

    private static void SetSpec(Dictionary<string, string> specs, string key, string? value)
    {
        value = value?.Trim();

        if (!string.IsNullOrEmpty(value))
        {
            specs[key] = value.Length > 500 ? value[..500] : value;
        }
    }

    private static string? StorageText(Payload data)
    {
        if (data.PhysicalDisks is { Count: > 0 })
        {
            return string.Join(" + ", data.PhysicalDisks
                .Where(d => d.SizeGb > 1)
                .Select(d => $"{FormatGb(d.SizeGb)} {MediaLabel(d.MediaType)}".Trim()));
        }

        return data.Disks is { Count: > 0 }
            ? string.Join(" + ", data.Disks.Where(d => d.SizeGb > 1).Select(d => $"{d.Drive} {FormatGb(d.SizeGb)}"))
            : null;
    }

    private static string MediaLabel(string? media) => media?.ToUpperInvariant() switch
    {
        "SSD" or "4" => "SSD",
        "HDD" or "3" => "HDD",
        _ => string.Empty
    };

    private static string FormatGb(double gb) =>
        gb >= 1000 ? $"{gb / 1000:0.#} TB" : $"{gb:0} GB";

    private static string? OsText(OsInfo? os)
    {
        if (os == null || string.IsNullOrWhiteSpace(os.Name))
        {
            return null;
        }

        var name = os.Name.Replace("Microsoft ", string.Empty).Trim();

        // Τα Windows 11 αναφέρουν «Windows 10» σε κάποια πεδία· το build το ξεκαθαρίζει.
        if (int.TryParse(os.Build, out var build) && build >= 22000 && name.Contains("Windows 10"))
        {
            name = name.Replace("Windows 10", "Windows 11");
        }

        var extra = string.Join(" ", new[] { os.DisplayVersion, string.IsNullOrWhiteSpace(os.Build) ? null : $"({os.Build})" }.Where(x => !string.IsNullOrWhiteSpace(x)));

        return string.IsNullOrWhiteSpace(extra) ? name : $"{name} {extra}";
    }

    private static bool IsJunk(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("To be filled", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("Default string", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("System Product Name", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase) ||
        value.Trim() is "0" or "None" or "N/A" or "123456789";

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > max ? text[..max] : text;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? date : null;
}
