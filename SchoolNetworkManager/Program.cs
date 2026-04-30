using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var isWindowsDesktop = OperatingSystem.IsWindows() && Environment.UserInteractive;
var windowsAppUrl = "http://localhost:5055";

if (isWindowsDesktop && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls(windowsAppUrl);
}

// Add services to the container.
builder.Services.AddRazorPages();

var appDataDir = Environment.GetEnvironmentVariable("APP_DATA_DIR");

if (string.IsNullOrWhiteSpace(appDataDir) && OperatingSystem.IsWindows())
{
    appDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SchoolNetworkManager",
        "data"
    );
}

var databasePath = string.IsNullOrWhiteSpace(appDataDir)
    ? "schoolnetworkmanager.db"
    : Path.Combine(appDataDir, "schoolnetworkmanager.db");

if (!string.IsNullOrWhiteSpace(appDataDir))
{
    Directory.CreateDirectory(appDataDir);
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    try
    {
        db.Database.ExecuteSqlRaw(
            "ALTER TABLE Computers ADD COLUMN MachineType TEXT NOT NULL DEFAULT 'Υπολογιστής';"
        );
    }
    catch
    {
        // Η στήλη υπάρχει ήδη.
    }

    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS AppSettings (
            Id INTEGER NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
            SchoolTitle TEXT NOT NULL,
            SchoolSubtitle TEXT NOT NULL,
            LogoPath TEXT NULL
        );
    """);

    var settingsExist = db.AppSettings.Any();

    if (!settingsExist)
    {
        db.AppSettings.Add(new AppSettings
        {
            Id = 1,
            SchoolTitle = "School Network Manager",
            SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
        });

        db.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

if (isWindowsDesktop && !app.Environment.IsDevelopment())
{
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = windowsAppUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            // Αν δεν ανοίξει browser, η εφαρμογή συνεχίζει κανονικά.
        }
    });
}

app.Run();