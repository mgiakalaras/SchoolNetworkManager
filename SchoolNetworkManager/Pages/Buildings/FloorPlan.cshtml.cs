using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Buildings;

/// <summary>Κάτοψη ορόφου: εικόνα φόντου και τοποθέτηση των χώρων του ορόφου (θέσεις σε ποσοστά 0–100).</summary>
public class FloorPlanModel : PageModel
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _environment;

    public FloorPlanModel(AppDbContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
    }

    public int FloorId { get; set; }

    public Floor Floor { get; set; } = new();

    public string BuildingName { get; set; } = string.Empty;

    /// <summary>Χώροι του ορόφου με την κατάστασή τους (ok / warn / none).</summary>
    public List<(RoomCard Card, string Status)> Rooms { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    private IActionResult Missing()
    {
        StatusMessage = "Ο όροφος δεν βρέθηκε.";
        StatusKind = "warning";

        return RedirectToPage("/Buildings/Index");
    }

    public async Task<IActionResult> OnGetAsync(int floorId)
    {
        var (buildings, _) = await BuildingQueries.LoadAsync(_context);

        var building = buildings.FirstOrDefault(b => b.Floors.Any(f => f.Floor.Id == floorId));
        var view = building?.Floors.First(f => f.Floor.Id == floorId);

        if (building == null || view == null)
        {
            return Missing();
        }

        FloorId = floorId;
        Floor = view.Floor;
        BuildingName = building.Building.Name;
        Rooms = view.Rooms.Select(c => (c, FloorPlanLayout.Status(c.Counts))).ToList();

        return Page();
    }

    // ---------------- Διάταξη ----------------

    public async Task<IActionResult> OnPostLayoutAsync(int floorId, string? layoutJson)
    {
        if (!await _context.Floors.AnyAsync(f => f.Id == floorId))
        {
            return NotFound();
        }

        var rooms = await _context.Rooms.Where(r => r.FloorId == floorId).ToListAsync();
        var boxes = FloorPlanLayout.Sanitise(layoutJson, rooms.Select(r => r.Id).ToList());

        if (boxes == null)
        {
            return new JsonResult(new { ok = false, message = "⚠️ Μη έγκυρη διάταξη — δεν αποθηκεύτηκε τίποτα." }) { StatusCode = 400 };
        }

        var byRoom = boxes.ToDictionary(b => b.RoomId);

        foreach (var room in rooms)
        {
            if (byRoom.TryGetValue(room.Id, out var b))
            {
                room.PlanX = b.X;
                room.PlanY = b.Y;
                room.PlanW = b.W;
                room.PlanH = b.H;
            }
            else
            {
                room.PlanX = room.PlanY = room.PlanW = room.PlanH = null;
            }
        }

        await _context.SaveChangesAsync();

        return new JsonResult(new { ok = true, placed = boxes.Count });
    }

    // ---------------- Αναλογία ----------------

    public async Task<IActionResult> OnPostAspectAsync(int floorId, double ratio)
    {
        var floor = await _context.Floors.FindAsync(floorId);

        if (floor == null)
        {
            return NotFound();
        }

        floor.AspectRatio = Math.Round(Math.Clamp(double.IsFinite(ratio) && ratio > 0 ? ratio : 1.6, 0.4, 4), 3);
        await _context.SaveChangesAsync();

        return new JsonResult(new { ok = true });
    }

    // ---------------- Εικόνα φόντου ----------------

    public async Task<IActionResult> OnPostBackgroundAsync(int floorId, IFormFile? file, bool remove)
    {
        var floor = await _context.Floors.FindAsync(floorId);

        if (floor == null)
        {
            return Missing();
        }

        if (remove)
        {
            floor.PlanImagePath = null;
            StatusMessage = "Η εικόνα της κάτοψης αφαιρέθηκε.";
            StatusKind = "success";
        }
        else if (file != null && file.Length > 0)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!new[] { ".png", ".jpg", ".jpeg", ".webp" }.Contains(extension) || file.Length > 8 * 1024 * 1024)
            {
                StatusMessage = "⚠️ Η εικόνα πρέπει να είναι PNG / JPG / WEBP έως 8MB.";
                StatusKind = "warning";

                return RedirectToPage(new { floorId });
            }

            // Ίδιος φάκελος με τις κατόψεις χώρων. Τα παλιά αρχεία δεν διαγράφονται (όπως και στο Locations/Plan).
            var folder = Path.Combine(_environment.WebRootPath, "uploads", "plans");
            Directory.CreateDirectory(folder);

            var fileName = $"plan_{Guid.NewGuid():N}{extension}";

            await using (var stream = new FileStream(Path.Combine(folder, fileName), FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            floor.PlanImagePath = $"/uploads/plans/{fileName}";
            StatusMessage = "🖼️ Η εικόνα της κάτοψης ανέβηκε.";
            StatusKind = "success";
        }

        await _context.SaveChangesAsync();

        return RedirectToPage(new { floorId, edit = true });
    }
}
