using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

public class AppSettings
{
    public const string DefaultPorts =
        "22:SSH, 80:Web, 139:NetBIOS, 443:HTTPS, 445:SMB, 515:LPD, 554:RTSP, 631:IPP, " +
        "3389:RDP, 5900:VNC, 8080:Web-alt, 9100:Printer, 11100:Veyon";

    public int Id { get; set; } = 1;

    // ---------- Γενικά ----------

    [Display(Name = "Τίτλος")]
    public string SchoolTitle { get; set; } = "School Network Manager";

    [Display(Name = "Υπότιτλος")]
    public string SchoolSubtitle { get; set; } = "Γρήγορη εικόνα του σχολικού δικτύου";

    [Display(Name = "Λογότυπο")]
    public string? LogoPath { get; set; }

    [Display(Name = "Εγγραφές ανά σελίδα")]
    public int PageSize { get; set; } = 25;

    [Display(Name = "Ένδειξη «ΝΕΟ» για (ημέρες)")]
    public int NewBadgeDays { get; set; } = 7;

    [Display(Name = "Χρήσιμα links της ομάδας")]
    public string? CustomLinks { get; set; }

    // Πότε πατήθηκε «Όλα ειδωμένα» — οι ενδείξεις ΝΕΟ/ΕΝΗΜ. μετρούν μόνο μετά από αυτό.
    public DateTime? BadgesClearedAt { get; set; }

    // ---------- Εμφάνιση ----------

    [Display(Name = "Προεπιλεγμένο θέμα")]
    public string? DefaultTheme { get; set; } = "light-blue";

    [Display(Name = "Εφέ φωτισμού στις κάρτες")]
    public bool EnableGlow { get; set; } = true;

    [Display(Name = "Εμφάνιση καιρού")]
    public bool ShowWeather { get; set; } = true;

    [Display(Name = "Τοποθεσία καιρού")]
    public string? WeatherPlace { get; set; } = "Σάμος";

    [Display(Name = "Γεωγρ. πλάτος")]
    public double WeatherLatitude { get; set; } = 37.69;

    [Display(Name = "Γεωγρ. μήκος")]
    public double WeatherLongitude { get; set; } = 26.94;

    // ---------- Δίκτυο ----------

    [Display(Name = "Εύρος σάρωσης")]
    public string? ScanRange { get; set; } = "192.168.2.1-254";

    [Display(Name = "Αυτόματος έλεγχος κάθε (λεπτά)")]
    public int MonitorIntervalMinutes { get; set; } = 10;

    [Display(Name = "Ports για έλεγχο")]
    public string? PortsToScan { get; set; } = DefaultPorts;

    [Display(Name = "Broadcast για Wake-on-LAN")]
    public string? WolBroadcast { get; set; } = "192.168.2.255";

    // ---------- MySpeed (ταχύτητα δικτύου) ----------

    [Display(Name = "Διεύθυνση MySpeed")]
    public string? MySpeedUrl { get; set; } = "http://127.0.0.1:5216";

    [Display(Name = "Κωδικός MySpeed (αν έχει)")]
    public string? MySpeedPassword { get; set; }

    // ---------- Pi-hole ----------

    [Display(Name = "Διεύθυνση Pi-hole")]
    public string? PiholeUrl { get; set; }

    [Display(Name = "Κωδικός / App password Pi-hole")]
    public string? PiholePassword { get; set; }

    // ---------- Βιβλίο βλαβών ----------

    [Display(Name = "Δημόσια σελίδα βλαβών (χωρίς κωδικό)")]
    public bool PublicFaultsPage { get; set; }

    [Display(Name = "Δημόσια σελίδα για μαθητές & εκπαιδευτικούς (αναφορά βλάβης + κανονισμοί)")]
    public bool PublicPortal { get; set; } = true;

    // ---------- Αυτόματα αντίγραφα ----------

    [Display(Name = "Αυτόματο αντίγραφο κάθε μέρα")]
    public bool AutoBackup { get; set; } = true;

    [Display(Name = "Ώρα")]
    public int AutoBackupHour { get; set; }

    [Display(Name = "Πόσα αυτόματα αντίγραφα κρατάω")]
    public int BackupKeep { get; set; } = 3;

    // ---------- Agent υπολογιστών ----------

    /// <summary>Κλειδί που στέλνει ο agent μαζί με κάθε αναφορά.</summary>
    public string? AgentKey { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
