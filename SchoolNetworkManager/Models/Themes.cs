namespace SchoolNetworkManager.Models;

public record ThemeOption(string Id, string Name, string Background, string Accent);

public static class Themes
{
    public const string DefaultId = "light-blue";

    public static readonly List<ThemeOption> All = new()
    {
        new("light-blue",   "Κλασικό",        "#f3f6fb", "#0d6efd"),
        new("dark-blue",    "Σκοτεινό",       "#0b1020", "#38bdf8"),
        new("light-teal",   "Θάλασσα",        "#eef8f7", "#0d9488"),
        new("dark-teal",    "Βυθός",          "#061a1c", "#2dd4bf"),
        new("light-green",  "Ελιά",           "#f3f6ec", "#4d7c0f"),
        new("dark-green",   "Terminal",       "#070d07", "#4ade80"),
        new("light-orange", "Ηλιοβασίλεμα",   "#fbf3ec", "#ea580c"),
        new("dark-orange",  "Κάρβουνο",       "#170e08", "#fb923c"),
        new("light-purple", "Λεβάντα",        "#f4f2fc", "#7c3aed"),
        new("dark-purple",  "Μεσάνυχτα",      "#0e0a1c", "#a78bfa"),
        new("light-rose",   "Τριαντάφυλλο",   "#fbf1f3", "#e11d48"),
        new("dark-rose",    "Κρασί",          "#17090e", "#fb7185"),
        new("light-mono",   "Χαρτί",          "#f4f4f3", "#374151"),
        new("dark-mono",    "Γραφίτης",       "#0f0f10", "#d1d5db")
    };

    public static string Normalize(string? id)
    {
        return All.Any(t => t.Id == id) ? id! : DefaultId;
    }
}
