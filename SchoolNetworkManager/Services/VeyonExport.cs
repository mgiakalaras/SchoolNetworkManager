using System.IO.Compression;
using System.Text;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Φτιάχνει zip για εισαγωγή χώρων και υπολογιστών στο Veyon (veyon-cli networkobjects import).
/// </summary>
public static class VeyonExport
{
    public const string Format = "%location%;%name%;%host%;%mac%";

    /// <summary>Ποιες συσκευές μπαίνουν στο Veyon: όσες έχουν Veyon ή είναι υπολογιστές / laptop.</summary>
    public static bool IsCandidate(Computer c) =>
        !c.IsEmptySlot &&
        (c.HasVeyon || c.MachineType == "Υπολογιστής" || c.MachineType == "Laptop");

    public static byte[] BuildZip(IEnumerable<Computer> computers, string schoolTitle)
    {
        var list = computers
            .Where(IsCandidate)
            .OrderBy(c => string.IsNullOrWhiteSpace(c.Location) ? "~" : c.Location)
            .ThenBy(c => NetworkTools.IpToNumber(c.IpAddress))
            .ToList();

        var csv = new StringBuilder();

        foreach (var c in list)
        {
            var location = Clean(string.IsNullOrWhiteSpace(c.Location) ? "Χωρίς χώρο" : c.Location);
            var name = Clean(string.IsNullOrWhiteSpace(c.Name) ? c.IpAddress : c.Name);
            var mac = NetworkTools.NormalizeMac(c.MacAddress) ?? string.Empty;

            csv.Append(location).Append(';')
               .Append(name).Append(';')
               .Append(c.IpAddress.Trim()).Append(';')
               .Append(mac.Length == 17 ? mac : string.Empty)
               .Append('\n');
        }

        using var memory = new MemoryStream();

        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            // UTF-8 χωρίς BOM: το Veyon διαβάζει κάθε γραμμή ως UTF-8 (ένα BOM θα «κολλούσε» στο όνομα του πρώτου χώρου)
            AddText(zip, "veyon-computers.csv", csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AddText(zip, "1-import-to-veyon.bat", ImportBat(clearFirst: false), new UTF8Encoding(false));
            AddText(zip, "2-replace-all-in-veyon.bat", ImportBat(clearFirst: true), new UTF8Encoding(false));
            AddText(zip, "README-VEYON.txt", Instructions(list.Count, schoolTitle), new UTF8Encoding(true));
        }

        return memory.ToArray();
    }

    private static string Clean(string value) =>
        value.Replace(";", ",").Replace("\r", " ").Replace("\n", " ").Trim();

    private static void AddText(ZipArchive zip, string name, string content, Encoding encoding)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, encoding);
        writer.Write(content);
    }

    private static string ImportBat(bool clearFirst)
    {
        var sb = new StringBuilder();
        sb.Append("@echo off\r\n");
        sb.Append("chcp 65001 >nul\r\n");
        sb.Append("title School Network Manager - Veyon import\r\n");
        sb.Append("net session >nul 2>&1\r\n");
        sb.Append("if errorlevel 1 (\r\n");
        sb.Append("  echo Please right-click this file and choose \"Run as administrator\".\r\n");
        sb.Append("  pause\r\n  exit /b 1\r\n)\r\n");
        sb.Append("set \"VEYON=%ProgramFiles%\\Veyon\\veyon-cli.exe\"\r\n");
        sb.Append("if not exist \"%VEYON%\" (\r\n");
        sb.Append("  echo Veyon was not found in \"%ProgramFiles%\\Veyon\".\r\n");
        sb.Append("  pause\r\n  exit /b 1\r\n)\r\n");

        if (clearFirst)
        {
            sb.Append("echo.\r\n");
            sb.Append("echo WARNING: this deletes ALL locations and computers currently in Veyon\r\n");
            sb.Append("echo and replaces them with the list from School Network Manager.\r\n");
            sb.Append("choice /M \"Continue\"\r\n");
            sb.Append("if errorlevel 2 exit /b 0\r\n");
            sb.Append("\"%VEYON%\" networkobjects clear\r\n");
        }

        sb.Append($"\"%VEYON%\" networkobjects import \"%~dp0veyon-computers.csv\" format \"{Format}\"\r\n");
        sb.Append("if errorlevel 1 (\r\n  echo Import failed.\r\n) else (\r\n  echo Done. Restart Veyon Master to see the computers.\r\n)\r\n");
        sb.Append("pause\r\n");

        return sb.ToString();
    }

    private static string Instructions(int count, string schoolTitle) => $"""
        Εξαγωγή για Veyon — {schoolTitle}
        Ημερομηνία: {DateTime.Now:dd/MM/yyyy HH:mm}
        Υπολογιστές: {count}

        ΤΙ ΠΕΡΙΕΧΕΙ
          veyon-computers.csv        χώρος;όνομα;IP;MAC για κάθε υπολογιστή
          1-import-to-veyon.bat      ΠΡΟΣΘΕΤΕΙ τους υπολογιστές στο Veyon
          2-replace-all-in-veyon.bat ΣΒΗΝΕΙ ό,τι έχει το Veyon και βάζει τη νέα λίστα

        ΒΗΜΑΤΑ (στον υπολογιστή του καθηγητή, όπου τρέχει το Veyon Master)
          1. Αποσυμπίεσε το zip σε έναν φάκελο.
          2. Veyon Configurator → Locations & computers: να είναι επιλεγμένο το
             «Builtin (computers and locations in local configuration)».
          3. Δεξί κλικ στο 1-import-to-veyon.bat → «Εκτέλεση ως διαχειριστής».
             (ή το 2-replace-all-in-veyon.bat αν θέλεις να ξεκινήσεις από την αρχή)
          4. Άνοιξε ξανά το Veyon Master.

        ΧΕΙΡΟΚΙΝΗΤΑ (αν προτιμάς γραμμή εντολών, ως διαχειριστής)
          "%ProgramFiles%\Veyon\veyon-cli.exe" networkobjects import veyon-computers.csv format "{Format}"

        Μπαίνουν όσες συσκευές έχουν τσεκαρισμένο το Veyon ή είναι τύπου Υπολογιστής / Laptop.
        """;
}
