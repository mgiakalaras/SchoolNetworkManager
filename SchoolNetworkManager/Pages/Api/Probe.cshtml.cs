using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Pages.Api;

/// <summary>
/// Επιστρέφει σε JSON ό,τι βρέθηκε για μια IP (χρησιμοποιείται από το κουμπί «Ανίχνευση» στις φόρμες).
/// </summary>
public class ProbeModel : PageModel
{
    private readonly DeviceProbe _probe;

    public ProbeModel(DeviceProbe probe)
    {
        _probe = probe;
    }

    public async Task<IActionResult> OnGetAsync(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return BadRequest(new { error = "Δώσε πρώτα IP." });
        }

        var result = await _probe.ProbeAsync(ip.Trim());

        return new JsonResult(new
        {
            ip = result.Ip,
            online = result.Online,
            mac = result.Mac,
            name = result.Name,
            vendor = result.Vendor,
            openPorts = result.OpenPortsText,
            ports = result.OpenPorts.Select(p => new { port = p.Port, label = p.Label }),
            suggestedType = result.SuggestedType,
            hasVeyon = result.HasVeyon
        });
    }
}
