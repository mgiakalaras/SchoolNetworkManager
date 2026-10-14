using System.Security.Cryptography;
using System.Text;

namespace SchoolNetworkManager.Services;

public class PasswordOptions
{
    public int Length { get; set; } = 14;
    public bool Upper { get; set; } = true;
    public bool Lower { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;
    public bool AvoidAmbiguous { get; set; } = true;
}

/// <summary>Κρυπτογραφικά ασφαλής γεννήτρια κωδικών (ίδιοι κανόνες με τη γεννήτρια της σελίδας).</summary>
public static class PasswordGenerator
{
    private const string UpperSet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string LowerSet = "abcdefghijklmnopqrstuvwxyz";
    private const string DigitSet = "0123456789";
    private const string SymbolSet = "!@#$%&*?-_=+";
    private const string Ambiguous = "0O1lI|";

    public static string Generate(PasswordOptions options)
    {
        var length = Math.Clamp(options.Length, 8, 64);
        var sets = new List<string>();

        if (options.Upper) sets.Add(UpperSet);
        if (options.Lower) sets.Add(LowerSet);
        if (options.Digits) sets.Add(DigitSet);
        if (options.Symbols) sets.Add(SymbolSet);
        if (sets.Count == 0) sets.Add(LowerSet);

        if (options.AvoidAmbiguous)
        {
            sets = sets.Select(s => new string(s.Where(ch => !Ambiguous.Contains(ch)).ToArray())).ToList();
        }

        var all = string.Concat(sets);
        var chars = new List<char>();

        // Τουλάχιστον ένας χαρακτήρας από κάθε ομάδα
        foreach (var set in sets)
        {
            chars.Add(set[RandomNumberGenerator.GetInt32(set.Length)]);
        }

        while (chars.Count < length)
        {
            chars.Add(all[RandomNumberGenerator.GetInt32(all.Length)]);
        }

        // Ανακάτεμα (Fisher–Yates)
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.Take(length).ToArray());
    }

    /// <summary>Εντολή για εφαρμογή σε τοπικό λογαριασμό Windows (γραμμή εντολών ως διαχειριστής).</summary>
    public static string NetUserCommand(string user, string password)
    {
        var u = user.Replace("\"", "");
        var p = password.Replace("\"", "\"\"");
        return $"net user \"{u}\" \"{p}\"";
    }
}
