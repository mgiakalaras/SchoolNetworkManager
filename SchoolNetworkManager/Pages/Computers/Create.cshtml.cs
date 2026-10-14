using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Computers;

public class CreateModel : PageModel
{
    private readonly AppDbContext _context;

    public CreateModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Computer Computer { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> SpecInput { get; set; } = new();

    /// <summary>Τιμές για την ενότητα «Χαρακτηριστικά» της φόρμας.</summary>
    public Dictionary<string, string> SpecInputView =>
        SpecInput.Count > 0
            ? SpecInput.Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value!)
            : Computer.SpecValues;

    public void OnGet(string? location)
    {
        // «Νέα καταχώρηση εδώ» από τη σελίδα Χώροι
        if (!string.IsNullOrWhiteSpace(location) && location != SchoolNetworkManager.Pages.Locations.IndexModel.NoLocationKey)
        {
            Computer.Location = location.Trim();
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Computer.IpAddress = Computer.IpAddress.Trim();
        
        if (string.IsNullOrWhiteSpace(Computer.MachineType))
        {
            Computer.MachineType = MachineTypes.Default;
        }

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ipExists = await _context.Computers
            .AnyAsync(c => c.IpAddress == Computer.IpAddress);

        if (ipExists)
        {
            ModelState.AddModelError("Computer.IpAddress", "Υπάρχει ήδη υπολογιστής με αυτή την IP.");
            return Page();
        }

        Computer.MacAddress = SchoolNetworkManager.Services.NetworkTools.NormalizeMac(Computer.MacAddress);
        Computer.Specs = DeviceSpecs.Serialize(SpecInput, Computer.MachineType, null);
        Computer.Comments = null;
        Computer.CreatedAt = DateTime.Now;
        Computer.UpdatedBy = SchoolNetworkManager.Services.UserPasswords.ActorName(User);

        _context.Computers.Add(Computer);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}