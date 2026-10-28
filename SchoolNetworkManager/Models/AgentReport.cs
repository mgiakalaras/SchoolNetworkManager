namespace SchoolNetworkManager.Models;

/// <summary>Η τελευταία αναφορά του agent ενός υπολογιστή (μία γραμμή ανά συσκευή).</summary>
public class AgentReport
{
    public int Id { get; set; }

    public int ComputerId { get; set; }

    public DateTime FirstReportAt { get; set; } = DateTime.Now;

    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    public string? AgentVersion { get; set; }

    public string? Hostname { get; set; }

    public string? LoggedUser { get; set; }

    public string? OsName { get; set; }

    public string? OsVersion { get; set; }

    public DateTime? LastBoot { get; set; }

    public string? CpuName { get; set; }

    public double? RamGb { get; set; }

    /// <summary>JSON: [{drive, label, sizeGb, freeGb}]</summary>
    public string? DisksJson { get; set; }

    /// <summary>Το μικρότερο ποσοστό ελεύθερου χώρου ανάμεσα στους τοπικούς δίσκους.</summary>
    public double? MinFreePercent { get; set; }

    public string? Manufacturer { get; set; }

    public string? Model { get; set; }

    public string? Serial { get; set; }

    public bool VeyonInstalled { get; set; }

    public bool VeyonRunning { get; set; }

    public string? VeyonVersion { get; set; }

    /// <summary>Όλες οι IPv4 / MAC που ανέφερε (για πληροφορία).</summary>
    public string? IpList { get; set; }

    public string? MacList { get; set; }

    public int SoftwareCount { get; set; }

    /// <summary>Το ID του RustDesk στον υπολογιστή (αν είναι εγκατεστημένο).</summary>
    public string? RustDeskId { get; set; }

    /// <summary>Μήνυμα αν δεν μπόρεσε να ενημερωθεί κάτι (π.χ. η IP ανήκει σε άλλη συσκευή).</summary>
    public string? Warning { get; set; }

    public bool IsStale => DateTime.Now - ReceivedAt > TimeSpan.FromDays(2);

    public bool LowDisk => MinFreePercent is < 10;
}

/// <summary>Πρόγραμμα εγκατεστημένο σε υπολογιστή (από τον agent).</summary>
public class InstalledSoftware
{
    public int Id { get; set; }

    public int ComputerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? Publisher { get; set; }

    public string? InstallDate { get; set; }

    /// <summary>machine = για όλους · user = μόνο για έναν λογαριασμό (συχνά ό,τι βάζουν οι μαθητές).</summary>
    public string? Scope { get; set; }

    public DateTime FirstSeen { get; set; } = DateTime.Now;
}
