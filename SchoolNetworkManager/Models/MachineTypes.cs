namespace SchoolNetworkManager.Models;

public static class MachineTypes
{
    public const string Default = "Υπολογιστής";

    public static readonly List<string> All = new()
    {
        "Υπολογιστής",
        "Laptop",
        "Tablet",
        "Κινητό",
        "Server",
        "Raspberry Pi / SBC",
        "Διαδραστικός Πίνακας",
        "Projector",
        "Εκτυπωτής",
        "Πολυμηχάνημα",
        "Switch",
        "Router",
        "Access Point",
        "NAS / Storage",
        "IP Camera / Webcam",
        "VoIP Τηλέφωνο",
        "UPS",
        "IoT / Arduino",
        "Άλλο",
        "Άγνωστο"
    };
}