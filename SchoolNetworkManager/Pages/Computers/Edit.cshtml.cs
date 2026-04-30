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
        existingComputer.MacAddress = Computer.MacAddress;
        existingComputer.Location = Computer.Location;
        existingComputer.Username = Computer.Username;
        existingComputer.Password = Computer.Password;
        existingComputer.Extras = Computer.Extras;
        existingComputer.Notes = Computer.Notes;
        existingComputer.Comments = Computer.Comments;
        existingComputer.HasLanMessenger = Computer.HasLanMessenger;
        existingComputer.HasVeyon = Computer.HasVeyon;
        existingComputer.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
        {
            return LocalRedirect(ReturnUrl);
        }

        return RedirectToPage("./Index");
    }
}