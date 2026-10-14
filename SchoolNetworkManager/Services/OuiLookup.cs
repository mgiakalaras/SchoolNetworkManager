namespace SchoolNetworkManager.Services;

/// <summary>
/// Βρίσκει τον κατασκευαστή μιας συσκευής από τα πρώτα ψηφία της MAC (μητρώο IEEE).
/// </summary>
public sealed class OuiLookup
{
    private readonly Lazy<Dictionary<string, string>> _data;

    public OuiLookup()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "oui.txt");
        _data = new Lazy<Dictionary<string, string>>(() => Load(path));
    }

    private static Dictionary<string, string> Load(string path)
    {
        var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(path))
            {
                return dictionary;
            }

            foreach (var line in File.ReadLines(path))
            {
                var tab = line.IndexOf('\t');

                if (tab > 0 && tab < line.Length - 1)
                {
                    dictionary[line.Substring(0, tab)] = line.Substring(tab + 1);
                }
            }
        }
        catch
        {
            // Χωρίς αρχείο απλά δεν εμφανίζεται κατασκευαστής.
        }

        return dictionary;
    }

    public string? Lookup(string? mac)
    {
        var normalized = NetworkTools.NormalizeMac(mac);

        if (normalized == null || normalized.Length != 17)
        {
            return null;
        }

        var hex = normalized.Replace(":", string.Empty);
        var firstByte = Convert.ToByte(hex.Substring(0, 2), 16);

        // Τοπικά διαχειριζόμενη (τυχαία) MAC — συνήθως κινητά με "ιδιωτική διεύθυνση".
        if ((firstByte & 0x02) != 0)
        {
            return "Τυχαία MAC (κινητό / ιδιωτική διεύθυνση)";
        }

        foreach (var length in new[] { 9, 7, 6 })
        {
            if (_data.Value.TryGetValue(hex.Substring(0, length), out var vendor))
            {
                return vendor;
            }
        }

        return null;
    }
}
