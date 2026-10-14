using System.Text.Json;

namespace SchoolNetworkManager.Models;

/// <summary>Ένα πεδίο χαρακτηριστικών (π.χ. «Επεξεργαστής»).</summary>
public record SpecField(string Key, string Label, string? Placeholder = null, string[]? Options = null, bool Wide = false);

/// <summary>Ομάδα πεδίων για συγκεκριμένους τύπους μηχανημάτων.</summary>
public record SpecGroup(string Id, string Title, string Icon, string[] Types, List<SpecField> Fields);

/// <summary>
/// Ποια χαρακτηριστικά εμφανίζονται για κάθε τύπο μηχανήματος.
/// Αποθηκεύονται όλα μαζί σε μία στήλη (JSON), οπότε για νέα πεδία αρκεί να προστεθούν εδώ.
/// </summary>
public static class DeviceSpecs
{
    private static readonly string[] Computers = { "Υπολογιστής", "Laptop", "Server" };
    private static readonly string[] Printers = { "Εκτυπωτής", "Πολυμηχάνημα" };
    private static readonly string[] NetworkGear = { "Switch", "Router", "Access Point" };

    /// <summary>Κοινά για όλες τις συσκευές (καταγραφή εξοπλισμού).</summary>
    public static readonly SpecGroup Common = new(
        "common", "Ταυτότητα εξοπλισμού", "🏷️", Array.Empty<string>(), new()
        {
            new("brand", "Μάρκα / Μοντέλο", "π.χ. Dell OptiPlex 3050"),
            new("serial", "Σειριακός αριθμός", "S/N"),
            new("inventory", "Αρ. απογραφής", "π.χ. ΕΠΑΛ-0123"),
            new("purchase", "Αγορά / Προέλευση", "π.χ. 2019 — Δωρεά"),
            new("warranty", "Εγγύηση έως", "π.χ. 12/2026")
        });

    public static readonly List<SpecGroup> TypeGroups = new()
    {
        new("pc", "Υλικό υπολογιστή", "🖥️", Computers, new()
        {
            new("cpu", "Επεξεργαστής", "π.χ. Intel Core i5-7500 @ 3.40GHz", Wide: true),
            new("cores", "Πυρήνες / Threads", "π.χ. 4 / 4"),
            new("ram", "Μνήμη RAM", "π.χ. 8 GB DDR4"),
            new("storage", "Δίσκοι", "π.χ. 240 GB SSD + 2 TB HDD", Wide: true),
            new("gpu", "Κάρτα γραφικών", "π.χ. Intel HD 630"),
            new("os", "Λειτουργικό", "π.χ. Windows 10 Pro 22H2"),
            new("monitor", "Οθόνη", "π.χ. AOC 22\""),
            new("peripherals", "Περιφερειακά", "π.χ. πληκτρολόγιο, ποντίκι, ακουστικά", Wide: true)
        }),
        new("laptop", "Φορητό", "🔋", new[] { "Laptop" }, new()
        {
            new("screen", "Οθόνη", "π.χ. 15.6\" FHD"),
            new("battery", "Μπαταρία", "π.χ. Καλή / Χρειάζεται αλλαγή"),
            new("charger", "Φορτιστής", "π.χ. Ναι, 65W USB-C"),
            new("assigned", "Χρεωμένο σε", "π.χ. Τμήμα / εκπαιδευτικό")
        }),
        new("printer", "Εκτυπωτής", "🖨️", Printers, new()
        {
            new("printtype", "Τεχνολογία", Options: new[] { "Laser", "Inkjet", "Θερμικός", "Άλλο" }),
            new("color", "Χρώμα", Options: new[] { "Ασπρόμαυρος", "Έγχρωμος" }),
            new("duplex", "Διπλής όψης", Options: new[] { "Ναι", "Όχι" }),
            new("paper", "Μέγεθος χαρτιού", "π.χ. A4 / A3"),
            new("consumable", "Toner / Μελάνι", "π.χ. HP 26A (CF226A)", Wide: true),
            new("pagecount", "Μετρητής σελίδων", "π.χ. 45.210"),
            new("scan", "Σάρωση σε", "π.χ. Email / Φάκελος δικτύου"),
            new("driver", "Driver / Σημείωση εγκατάστασης", Wide: true)
        }),
        new("projector", "Προβολή", "📽️", new[] { "Projector", "Διαδραστικός Πίνακας" }, new()
        {
            new("resolution", "Ανάλυση", "π.χ. 1280×800"),
            new("lumens", "Φωτεινότητα", "π.χ. 3300 lumens"),
            new("lamp", "Ώρες λάμπας", "π.χ. 2.100 / 5.000"),
            new("inputs", "Είσοδοι", "π.χ. HDMI, VGA"),
            new("touch", "Αφή / Στυλό", "π.χ. 10 σημείων"),
            new("connectedpc", "Συνδεδεμένος υπολογιστής", "π.χ. PC-ΚΑΘΗΓΗΤΗ")
        }),
        new("network", "Δικτυακός εξοπλισμός", "🔀", NetworkGear, new()
        {
            new("ports", "Θύρες", "π.χ. 24 × 1Gbps + 2 SFP"),
            new("managed", "Διαχειρίσιμο", Options: new[] { "Ναι", "Όχι" }),
            new("poe", "PoE", Options: new[] { "Ναι", "Όχι", "Μερικώς" }),
            new("speed", "Ταχύτητα", "π.χ. 1 Gbps"),
            new("ssid", "SSID / VLAN", "π.χ. EPAL-LAB / VLAN 20"),
            new("firmware", "Firmware", "π.χ. v2.1.4"),
            new("uplink", "Uplink προς", "π.χ. Switch Πίνακα, θύρα 24", Wide: true),
            new("admin", "Σελίδα διαχείρισης", "π.χ. http://192.168.2.1")
        }),
        new("mobile", "Φορητή συσκευή", "📱", new[] { "Tablet", "Κινητό" }, new()
        {
            new("os", "Λειτουργικό", "π.χ. Android 13 / iPadOS 17"),
            new("storage", "Αποθηκευτικός χώρος", "π.χ. 64 GB"),
            new("screen", "Οθόνη", "π.χ. 10.1\""),
            new("assigned", "Χρεωμένο σε", "π.χ. Τμήμα / μαθητή")
        }),
        new("nas", "Αποθήκευση / Server", "🗄️", new[] { "NAS / Storage", "Server" }, new()
        {
            new("disks", "Δίσκοι / RAID", "π.χ. 2 × 4 TB, RAID 1", Wide: true),
            new("capacity", "Χωρητικότητα", "π.χ. 4 TB"),
            new("services", "Υπηρεσίες", "π.χ. Pi-hole, WordPress, Portainer", Wide: true),
            new("webui", "Σελίδα διαχείρισης", "π.χ. http://192.168.2.114")
        }),
        new("sbc", "Πλακέτα / IoT", "🔌", new[] { "Raspberry Pi / SBC", "IoT / Arduino" }, new()
        {
            new("board", "Πλακέτα", "π.χ. Raspberry Pi 4 · 4 GB"),
            new("os", "Λειτουργικό / Firmware", "π.χ. Raspberry Pi OS"),
            new("storage", "Κάρτα / Αποθήκευση", "π.χ. microSD 32 GB"),
            new("purpose", "Χρήση", "π.χ. Όμιλος Ρομποτικής", Wide: true)
        }),
        new("camera", "Κάμερα", "📷", new[] { "IP Camera / Webcam" }, new()
        {
            new("resolution", "Ανάλυση", "π.χ. 1080p"),
            new("stream", "Διεύθυνση ροής", "π.χ. rtsp://…", Wide: true),
            new("recording", "Καταγραφή σε", "π.χ. NVR / NAS")
        }),
        new("ups", "UPS", "🔋", new[] { "UPS" }, new()
        {
            new("capacity", "Ισχύς", "π.χ. 1500 VA / 900 W"),
            new("battery", "Μπαταρία", "π.χ. Αλλαγή 03/2025"),
            new("protects", "Προστατεύει", "π.χ. Server, Switch", Wide: true)
        }),
        new("voip", "Τηλέφωνο", "☎️", new[] { "VoIP Τηλέφωνο" }, new()
        {
            new("extension", "Εσωτερικό", "π.χ. 205"),
            new("line", "Γραμμή / Πάροχος", "π.χ. ΣΥΖΕΥΞΙΣ")
        })
    };

    public static IEnumerable<SpecGroup> AllGroups => new[] { Common }.Concat(TypeGroups);

    public static List<SpecGroup> GroupsFor(string? machineType)
    {
        var groups = new List<SpecGroup>();

        groups.AddRange(TypeGroups.Where(g => g.Types.Contains(machineType ?? MachineTypes.Default)));
        groups.Add(Common);

        return groups;
    }

    /// <summary>Μοναδικό όνομα πεδίου φόρμας (ομάδα + κλειδί) ώστε ίδια κλειδιά σε διαφορετικές ομάδες να μη συγκρούονται.</summary>
    public static string FormKey(SpecGroup group, SpecField field) => $"{group.Id}_{field.Key}";

    public static Dictionary<string, string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Κρατά μόνο τα πεδία που αντιστοιχούν στον τύπο, χωρίς κενά.
    /// Πεδία άλλων τύπων που υπήρχαν ήδη διατηρούνται (αν αλλάξει κατά λάθος ο τύπος, δεν χάνονται).
    /// </summary>
    public static string? Serialize(Dictionary<string, string?>? posted, string? machineType, string? previousJson)
    {
        var result = Parse(previousJson);
        var allowed = GroupsFor(machineType)
            .SelectMany(g => g.Fields.Select(f => FormKey(g, f)))
            .ToHashSet();

        if (posted != null)
        {
            foreach (var key in allowed)
            {
                posted.TryGetValue(key, out var value);
                value = value?.Trim();

                if (string.IsNullOrEmpty(value))
                {
                    result.Remove(key);
                }
                else
                {
                    result[key] = value.Length > 500 ? value.Substring(0, 500) : value;
                }
            }
        }

        return result.Count == 0 ? null : JsonSerializer.Serialize(result);
    }

    public static string TypeIcon(string? machineType) => machineType switch
    {
        "Υπολογιστής" => "🖥️",
        "Laptop" => "💻",
        "Tablet" => "📱",
        "Κινητό" => "📱",
        "Server" => "🗄️",
        "Raspberry Pi / SBC" => "🍓",
        "Διαδραστικός Πίνακας" => "🧑‍🏫",
        "Projector" => "📽️",
        "Εκτυπωτής" => "🖨️",
        "Πολυμηχάνημα" => "🖨️",
        "Switch" => "🔀",
        "Router" => "🌐",
        "Access Point" => "📶",
        "NAS / Storage" => "💾",
        "IP Camera / Webcam" => "📷",
        "VoIP Τηλέφωνο" => "☎️",
        "UPS" => "🔋",
        "IoT / Arduino" => "🔌",
        _ => "📦"
    };
}
