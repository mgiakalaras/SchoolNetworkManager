using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

public static class SchoolDocuments
{
    /// <summary>Τα δύο κείμενα (δημιουργούνται με αρχικό περιεχόμενο την πρώτη φορά).</summary>
    public static async Task<List<SchoolDocument>> EnsureAsync(AppDbContext db)
    {
        var existing = await db.Documents.ToListAsync();
        var added = false;

        foreach (var key in SchoolDocument.Keys)
        {
            var current = existing.FirstOrDefault(d => d.Key == key);

            if (current != null)
            {
                // Αν έμεινε απείραχτο το πρώτο πρότυπο, μπαίνουν τα κείμενα του σχολείου.
                if (DocText.IsLegacyDefault(key, current.Body))
                {
                    (current.Title, current.Body) = DocText.Default(key);
                    current.UpdatedAt = DateTime.Now;
                    added = true;
                }

                continue;
            }

            var (title, body) = DocText.Default(key);
            var document = new SchoolDocument { Key = key, Title = title, Body = body, IsPublic = true, UpdatedAt = DateTime.Now };
            db.Documents.Add(document);
            existing.Add(document);
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync();
        }

        return SchoolDocument.Keys.Select(k => existing.First(d => d.Key == k)).ToList();
    }

    public static string ShortName(string key) =>
        key == SchoolDocument.StudentRules ? "Κανόνες μαθητών" : "Κανονισμός εργαστηρίων";
}
