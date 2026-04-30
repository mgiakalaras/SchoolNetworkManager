using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Pages.Computers;

public class DetailsModel : PageModel
{
    private readonly AppDbContext _context;

    public DetailsModel(AppDbContext context)
    {
        _context = context;
    }

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
}