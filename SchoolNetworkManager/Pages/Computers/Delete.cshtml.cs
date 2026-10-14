using Microsoft.EntityFrameworkCore;
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

        // Ιστορικό και βλάβες της συσκευής
        await _context.DeviceHours.Where(h => h.ComputerId == computer.Id).ExecuteDeleteAsync();
        await _context.Faults.Where(f => f.ComputerId == computer.Id).ExecuteDeleteAsync();
        await _context.PlanItems.Where(p => p.ComputerId == computer.Id).ExecuteDeleteAsync();
        await _context.Seats.Where(s => s.ComputerId == computer.Id).ExecuteDeleteAsync();
        await _context.FaultReports.Where(r => r.ComputerId == computer.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.ComputerId, (int?)null));

        return RedirectToPage("./Index");
    }
}