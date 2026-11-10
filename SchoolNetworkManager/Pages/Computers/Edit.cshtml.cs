using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Computers;

public class EditModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly RoomService _rooms;

    public EditModel(AppDbContext context, RoomService rooms)
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
        await PrepareRoomSelectAsync(computer.RoomId, computer.Location, posted: false);

        return Page();
    }

    /// <summary>Επιλεγμένος χώρος στη λίστα· με <see cref="RoomService.LegacyRoomId"/> = παλιός χώρος.</summary>
    public int? SelectedRoomId { get; set; }

    /// <summary>Παλιό κείμενο χώρου χωρίς εγγραφή χώρου (βάση χωρίς μετατροπή) — εμφανίζεται ως επιλογή.</summary>
    public string? LegacyRoomName { get; set; }

    /// <summary>
    /// Λίστα χώρων. Συσκευή χωρίς RoomId με κείμενο Location: προεπιλέγεται ο χώρος με αυτό το όνομα
    /// ή η επιλογή «(παλιός χώρος)», ώστε η αποθήκευση να μη σβήνει τον χώρο. Σε επανεμφάνιση μετά
    /// από POST κρατιέται ό,τι διάλεξε ο χρήστης (και το «χωρίς χώρο»).
    /// </summary>
    private async Task PrepareRoomSelectAsync(int? roomId, string? location, bool posted)
    {
        RoomOptions = await Services.RoomOptions.LoadAsync(_context);
        (SelectedRoomId, LegacyRoomName) = await _rooms.SelectionAsync(roomId, location, posted);
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        Computer.IpAddress = (Computer.IpAddress ?? string.Empty).Trim();

        if (!ModelState.IsValid)
        {
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
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
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
            return Page();
        }

        try
        {
            await _rooms.AssignAsync(existingComputer, Computer.RoomId);
        }
        catch (ArgumentException)
        {
            ModelState.AddModelError("Computer.RoomId", "Ο χώρος δεν υπάρχει πια.");
            await PrepareRoomSelectAsync(Computer.RoomId, Computer.Location, posted: true);
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