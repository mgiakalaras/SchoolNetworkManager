using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SchoolNetworkManager.Pages;

public class ExitModel : PageModel
{
    private readonly IHostApplicationLifetime _applicationLifetime;

    public ExitModel(IHostApplicationLifetime applicationLifetime)
    {
        _applicationLifetime = applicationLifetime;
    }

    public bool IsClosing { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        // Σε server (Docker/ZimaOS) η εφαρμογή δεν κλείνει από τη σελίδα.
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return RedirectToPage("/Index");
        }

        IsClosing = true;

        Task.Run(async () =>
        {
            await Task.Delay(1200);
            _applicationLifetime.StopApplication();
        });

        return Page();
    }
}