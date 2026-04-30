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

    public void OnGet()
    {
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

        Computer.CreatedAt = DateTime.Now;

        _context.Computers.Add(Computer);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}