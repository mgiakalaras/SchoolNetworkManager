using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Computers;

public class DeleteModel : PageModel
{
    private readonly AppDbContext _context;

    public DeleteModel(AppDbContext context)
    {
        _context = context;
    }

    [BindProperty]
    public Computer Computer { get; set; } = new();

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
        var computer = await _context.Computers.FindAsync(id);

        if (computer == null)
        {
            return NotFound();
        }

        _context.Computers.Remove(computer);
        await _context.SaveChangesAsync();

        return RedirectToPage("./Index");
    }
}