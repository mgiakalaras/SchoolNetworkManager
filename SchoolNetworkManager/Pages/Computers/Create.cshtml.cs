using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Computers;

public class CreateModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly RoomService _rooms;

    public CreateModel(AppDbContext context, RoomService rooms)
    {
        _context = context;
        _rooms = rooms;
    }

    /// <summary>Επιλογές για τη λίστα «Χώρος».</summary>
    public List<RoomOption> RoomOptions { get; set; } = new();

    [BindProperty]
    public Computer Computer { get; set; } = new();

    [BindProperty]
    public Dictionary<string, string?> SpecInput { get; set; } = new();

    /// <summary>Τιμές για την ενότητα «Χαρακτηριστικά» της φόρμας.</summary>
    public Dictionary<string, string> SpecInputView =>
        SpecInput.Count > 0
            ? SpecInput.Where(x => x.Value != null).ToDictionary(x => x.Key, x => x.Value!)
            : Computer.SpecValues;

    public async Task OnGetAsync(string? location)
    {
        // «Νέα καταχώρηση εδώ» από τη σελίδα Χώροι: το όνομα γίνεται επιλεγμένος χώρος
        // (ή «παλιός χώρος» αν δεν υπάρχει ακόμη εγγραφή χώρου — βάση χωρίς μετατροπή).
        var name = location == SchoolNetworkManager.Pages.Locations.IndexModel.NoLocationKey ? null : location;
        await PrepareRoomSelectAsync(null, name, posted: false);
    }

    /// <summary>Επιλεγμένος χώρος στη λίστα· με <see cref="RoomService.LegacyRoomId"/> = παλιός χώρος.</summary>
    public int? SelectedRoomId { get; set; }

    /// <summary>Όνομα χώρου χωρίς εγγραφή χώρου (βάση χωρίς μετατροπή) — εμφανίζεται ως επιλογή.</summary>
    public string? LegacyRoomName { get; set; }

    private async Task PrepareRoomSelectAsync(int? roomId, string? location, bool posted)
    {
        RoomOptions = await Services.RoomOptions.LoadAsync(_context);
        (SelectedRoomId, LegacyRoomName) = await _rooms.SelectionAsync(roomId, location, posted);
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
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
            return Page();
        }

        var ipExists = await _context.Computers
            .AnyAsync(c => c.IpAddress == Computer.IpAddress);

        if (ipExists)
        {
            ModelState.AddModelError("Computer.IpAddress", "Υπάρχει ήδη υπολογιστής με αυτή την IP.");
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
            return Page();
        }

        try
        {
            await _rooms.AssignAsync(Computer, Computer.RoomId);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError("Computer.RoomId", "Ο χώρος δεν υπάρχει πια.");
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
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