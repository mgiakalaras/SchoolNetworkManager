namespace SchoolNetworkManager.Models;

/// <summary>
/// Ιστορικό λειτουργίας: μία γραμμή ανά συσκευή και ανά ώρα.
/// Κρατά πόσοι έλεγχοι έγιναν, σε πόσους ήταν online και πόσες φορές «έπεσε» (online → offline).
/// </summary>
public class DeviceHour
{
    public int Id { get; set; }
    public int ComputerId { get; set; }
    public DateTime Hour { get; set; }
    public int Checks { get; set; }
    public int OnlineChecks { get; set; }
    public int Drops { get; set; }
}
