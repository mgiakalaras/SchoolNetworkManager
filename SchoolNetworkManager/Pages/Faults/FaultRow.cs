using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Faults;

public class FaultRow
{
    public Fault Fault { get; set; } = new();
    public Computer? Computer { get; set; }

    public string DeviceLabel => Computer == null
        ? "(διαγραμμένη συσκευή)"
        : string.IsNullOrWhiteSpace(Computer.Name) ? Computer.IpAddress : Computer.Name;

    public string LocationLabel => string.IsNullOrWhiteSpace(Computer?.Location) ? "Χωρίς χώρο" : Computer!.Location!.Trim();
}
