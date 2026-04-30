using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Computers;

public class ImportModel : PageModel
{
    private readonly AppDbContext _context;

    public ImportModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public string? ResultMessage { get; set; }

    public List<string> Errors { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (UploadFile == null || UploadFile.Length == 0)
        {
            ModelState.AddModelError("UploadFile", "Διάλεξε πρώτα ένα αρχείο Excel.");
            return Page();
        }

        if (!UploadFile.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("UploadFile", "Το αρχείο πρέπει να είναι .xlsx.");
            return Page();
        }

        var imported = 0;
        var updated = 0;
        var skipped = 0;

        using var stream = UploadFile.OpenReadStream();
        using var workbook = new XLWorkbook(stream);

        var worksheet = workbook.Worksheets.FirstOrDefault();

        if (worksheet == null)
        {
            ModelState.AddModelError("UploadFile", "Δεν βρέθηκε φύλλο εργασίας στο Excel.");
            return Page();
        }

        var headerRow = worksheet.Row(1);
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;

        if (lastColumn == 0 || lastRow < 2)
        {
            ModelState.AddModelError("UploadFile", "Το Excel δεν φαίνεται να έχει δεδομένα.");
            return Page();
        }

        var headers = new Dictionary<string, int>();

        for (int col = 1; col <= lastColumn; col++)
        {
            var header = NormalizeHeader(headerRow.Cell(col).GetString());

            if (!string.IsNullOrWhiteSpace(header) && !headers.ContainsKey(header))
            {
                headers.Add(header, col);
            }
        }

        var ipColumn = FindColumn(headers, "ipaddress", "ip", "ipaddr");

        if (ipColumn == null)
        {
            ModelState.AddModelError("UploadFile", "Δεν βρέθηκε στήλη IP Address στο Excel.");
            return Page();
        }

        for (int rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);

            var ip = GetCell(row, ipColumn.Value).Trim();

            if (string.IsNullOrWhiteSpace(ip))
            {
                skipped++;
                continue;
            }

            var existingComputer = await _context.Computers
                .FirstOrDefaultAsync(c => c.IpAddress == ip);

            if (existingComputer == null)
            {
                existingComputer = new Computer
                {
                    IpAddress = ip,
                    CreatedAt = DateTime.Now
                };

                _context.Computers.Add(existingComputer);
                imported++;
            }
            else
            {
                existingComputer.UpdatedAt = DateTime.Now;
                updated++;
            }

            existingComputer.Name = GetValue(headers, row, "name", "computername", "pcname", "hostname");
            existingComputer.MacAddress = GetValue(headers, row, "macaddress", "mac");
            existingComputer.Location = GetValue(headers, row, "location", "room", "place");
            existingComputer.Notes = GetValue(headers, row, "notes", "note");
            existingComputer.Username = GetValue(headers, row, "username", "user");
            existingComputer.Password = GetValue(headers, row, "password", "pass");
            existingComputer.Extras = GetValue(headers, row, "extras", "extra");
            existingComputer.Comments = GetValue(headers, row, "σημειωσεις", "comments", "comment");

            existingComputer.HasLanMessenger = GetBoolValue(headers, row, "lanmessenger", "lan");
            existingComputer.HasVeyon = GetBoolValue(headers, row, "veyon");
        }

        await _context.SaveChangesAsync();

        ResultMessage = $"Το import ολοκληρώθηκε. Νέες εγγραφές: {imported}, ενημερώσεις: {updated}, αγνοημένες γραμμές: {skipped}.";

        return Page();
    }

    private static string NormalizeHeader(string value)
    {
        return value
            .Trim()
            .ToLowerInvariant()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .Replace(".", "")
            .Replace("ά", "α")
            .Replace("έ", "ε")
            .Replace("ή", "η")
            .Replace("ί", "ι")
            .Replace("ό", "ο")
            .Replace("ύ", "υ")
            .Replace("ώ", "ω");
    }

    private static int? FindColumn(Dictionary<string, int> headers, params string[] names)
    {
        foreach (var name in names)
        {
            var normalized = NormalizeHeader(name);

            if (headers.TryGetValue(normalized, out var column))
            {
                return column;
            }
        }

        return null;
    }

    private static string GetValue(Dictionary<string, int> headers, IXLRow row, params string[] names)
    {
        var column = FindColumn(headers, names);

        if (column == null)
        {
            return string.Empty;
        }

        return GetCell(row, column.Value).Trim();
    }

    private static string GetCell(IXLRow row, int column)
    {
        return row.Cell(column).GetFormattedString();
    }

    private static bool GetBoolValue(Dictionary<string, int> headers, IXLRow row, params string[] names)
    {
        var value = GetValue(headers, row, names)
            .Trim()
            .ToLowerInvariant();

        return value == "1"
            || value == "yes"
            || value == "true"
            || value == "ναι"
            || value == "nai"
            || value == "x"
            || value == "✓";
    }
}