using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Computers;

public class EditModel : PageModel
{
    private readonly AppDbContext _context;

    public EditModel(AppDbContext context)
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
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        Computer = computer;

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        Computer.IpAddress = (Computer.IpAddress ?? string.Empty).Trim();

        if (!ModelState.IsValid)
        {
            return Page();
        }

        var existingComputer = await _context.Computers.FindAsync(id);

        if (existingComputer == null)
        {
            return NotFound();
        }

        var ipExists = await _context.Computers
            .AnyAsync(c => c.Id != id && c.IpAddress == Computer.IpAddress);

        if (ipExists)
        {
            ModelState.AddModelError("Computer.IpAddress", "Υπάρχει ήδη άλλος υπολογιστής με αυτή την IP.");
            return Page();
        }

        existingComputer.IpAddress = Computer.IpAddress;
        existingComputer.MachineType = string.IsNullOrWhiteSpace(Computer.MachineType)
            ? MachineTypes.Default
            : Computer.MachineType;

        existingComputer.Name = Computer.Name;
        existingComputer.MacAddress = SchoolNetworkManager.Services.NetworkTools.NormalizeMac(Computer.MacAddress);
        existingComputer.Vendor = Computer.Vendor;
        existingComputer.OpenPorts = Computer.OpenPorts;
        existingComputer.Location = Computer.Location;
        existingComputer.Username = Computer.Username;
        existingComputer.Password = Computer.Password;
        existingComputer.Extras = Computer.Extras;
        existingComputer.Notes = Computer.Notes;
        existingComputer.Comments = null;
        existingComputer.Specs = DeviceSpecs.Serialize(SpecInput, existingComputer.MachineType, existingComputer.Specs);
        existingComputer.HasLanMessenger = Computer.HasLanMessenger;
        existingComputer.HasVeyon = Computer.HasVeyon;
        existingComputer.IsDynamic = Computer.IsDynamic;
        existingComputer.NoAutoShutdown = Computer.NoAutoShutdown;
        existingComputer.UpdatedAt = DateTime.Now;
        existingComputer.UpdatedBy = SchoolNetworkManager.Services.UserPasswords.ActorName(User);

        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("./Index");
    }
}