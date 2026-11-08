using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Παραλαβή αναφορών από το LabLock (το εργαλείο κλειδώματος των εργαστηρίων, github.com/mgiakalaras/LabLock).
/// Βρίσκει τη συσκευή όπως ο agent (MAC → IP → όνομα), ή τη δημιουργεί, και κρατά την τελευταία κατάσταση κλειδώματος.
/// Χρησιμοποιεί το ίδιο κλειδί με τον agent.
/// </summary>
public static class LabLockIngest
{
    public const string LabLockUser = "lablock";

    public class Payload
    {
        /// <summary>Δοκιμή σύνδεσης από το LabLock: ελέγχεται μόνο το κλειδί.</summary>
        public bool Test { get; set; }
        public string? LabLockVersion { get; set; }
        public string? Event { get; set; }
        public string? Hostname { get; set; }
        public string? Student { get; set; }
        public List<string>? Ips { get; set; }
        public List<string>? Macs { get; set; }
        public List<LabLockItem>? Items { get; set; }
    }

    public static async Task<AgentIngest.Result> ProcessAsync(AppDbContext db, Payload data, string? remoteIp)
    {
        if (data.Test)
        {
            return new AgentIngest.Result(true, "Η σύνδεση με το School Network Manager δουλεύει.");
        }

        var hostname = Clean(data.Hostname, 64);

        if (hostname == null)
        {
            return new AgentIngest.Result(false, "Λείπει το όνομα υπολογιστή.");
        }

        var macs = (data.Macs ?? new List<string>())
            .Select(NetworkTools.NormalizeMac)
            .Where(NetworkTools.IsUsableMac)
            .Select(m => m!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ips = (data.Ips ?? new List<string>())
            .Select(ip => ip.Trim())
            .Where(ip => IPAddress.TryParse(ip, out var a) && a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                         !ip.StartsWith("169.254.") && !ip.StartsWith("127."))
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(remoteIp) && !ips.Contains(remoteIp) && ips.Count == 0)
        {
            ips.Add(remoteIp);
        }

        var all = await db.Computers.ToListAsync();

        // 1) MAC  2) IP  3) όνομα — όπως ο agent
        var computer = all.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.MacAddress) &&
                                               macs.Contains(NetworkTools.NormalizeMac(c.MacAddress)!, StringComparer.OrdinalIgnoreCase))
                       ?? all.FirstOrDefault(c => ips.Contains(c.IpAddress.Trim()))
                       ?? all.FirstOrDefault(c => !c.IsDynamic && string.Equals(c.Name?.Trim(), hostname, StringComparison.OrdinalIgnoreCase));

        var now = DateTime.Now;
        var created = false;

        if (computer == null)
        {
            var ip = ips.FirstOrDefault();

            if (ip == null)
            {
                return new AgentIngest.Result(false, "Ο υπολογιστής δεν βρέθηκε και δεν έστειλε διεύθυνση IP.");
            }

            computer = new Computer
            {
                IpAddress = ip,
                Name = hostname,
                MacAddress = macs.FirstOrDefault(),
                MachineType = MachineTypes.Default,
                CreatedAt = now,
                UpdatedBy = LabLockUser
            };

            db.Computers.Add(computer);
            await db.SaveChangesAsync();
            created = true;
        }
        else if (string.IsNullOrWhiteSpace(computer.MacAddress) && macs.Count > 0)
        {
            computer.MacAddress = macs[0];
            computer.UpdatedAt = now;
            computer.UpdatedBy = LabLockUser;
        }

        var items = (data.Items ?? new List<LabLockItem>())
            .Where(i => !string.IsNullOrWhiteSpace(i.Id))
            .Take(200)
            .Select(i => new LabLockItem
            {
                Id = Clean(i.Id, 64),
                Group = Clean(i.Group, 80),
                Title = Clean(i.Title, 200),
                State = Clean(i.State, 200),
                Kind = i.Kind is "applied" or "partly" or "off" ? i.Kind : "unknown",
                Recorded = Clean(i.Recorded, 32)
            })
            .ToList();

        var status = await db.LabLockStatuses.FirstOrDefaultAsync(s => s.ComputerId == computer.Id);

        if (status == null)
        {
            status = new LabLockStatus { ComputerId = computer.Id };
            db.LabLockStatuses.Add(status);
        }

        status.ReceivedAt = now;
        status.LabLockVersion = Clean(data.LabLockVersion, 20);
        status.Event = Clean(data.Event, 20);
        status.Hostname = hostname;
        status.Student = Clean(data.Student, 64);
        status.ItemsJson = JsonSerializer.Serialize(items);
        status.AppliedCount = items.Count(i => i.Kind == "applied");
        status.TotalCount = items.Count;

        await db.SaveChangesAsync();

        var name = computer.Name ?? computer.IpAddress;
        return new AgentIngest.Result(true,
            created ? $"Νέα συσκευή «{name}» με {status.AppliedCount} ρυθμίσεις κλειδώματος." : $"Ενημερώθηκε η συσκευή «{name}» ({status.AppliedCount}/{status.TotalCount} ρυθμίσεις).",
            computer.Id, created);
    }

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > max ? text[..max] : text;
    }
}
