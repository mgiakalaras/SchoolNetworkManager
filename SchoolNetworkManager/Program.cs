using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var isWindowsDesktop = OperatingSystem.IsWindows() && Environment.UserInteractive;
var windowsAppUrl = "http://localhost:5055";

if (isWindowsDesktop && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls(windowsAppUrl);
}

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

// Κωδικός πρόσβασης: ενεργοποιείται μόνο αν οριστεί ADMIN_PASSWORD (π.χ. στον server).
// Στην έκδοση Windows χωρίς ADMIN_PASSWORD η εφαρμογή δουλεύει όπως πριν, χωρίς σύνδεση.
var authEnabled = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ADMIN_PASSWORD"));

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    if (authEnabled)
    {
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AuthorizeFolder("/Users", "AdminOnly");
        options.Conventions.AuthorizeFolder("/Backups", "AdminOnly");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/Error");

        // Η δημόσια σελίδα βλαβών ελέγχει μόνη της αν επιτρέπεται (Ρυθμίσεις).
        options.Conventions.AllowAnonymousToPage("/Faults/Public");

        // Δημόσια σελίδα για μαθητές / εκπαιδευτικούς: δήλωση βλάβης και κανονισμοί.
        options.Conventions.AllowAnonymousToFolder("/Public");
    }
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.Cookie.Name = "snm_auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.AccessDeniedPath = "/Account";

        // Προσωπικοί λογαριασμοί: αν ο χρήστης σβηστεί, απενεργοποιηθεί ή αλλάξει κωδικό,
        // οι ανοιχτές συνδέσεις του κλείνουν στο επόμενο κλικ.
        options.Events.OnValidatePrincipal = async context =>
        {
            var userId = UserPasswords.UserId(context.Principal);

            if (userId == null)
            {
                // Συνδέσεις από παλιότερη έκδοση: ο κοινός admin παίρνει τον ρόλο διαχειριστή.
                if (context.Principal?.Identity?.Name == UserPasswords.MasterUserName &&
                    !context.Principal.IsInRole(UserPasswords.RoleAdmin))
                {
                    context.ReplacePrincipal(UserPasswords.Principal(null));
                    context.ShouldRenew = true;
                }

                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var stamp = context.Principal?.FindFirst(UserPasswords.ClaimStamp)?.Value;
            var account = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value);

            if (account == null || account.IsDisabled || account.SecurityStamp != stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(UserPasswords.RoleAdmin));
});

// Τα κλειδιά των cookies αποθηκεύονται στον φάκελο δεδομένων,
// ώστε να μη χρειάζεται νέα σύνδεση μετά από κάθε επανεκκίνηση.
if (!string.IsNullOrWhiteSpace(appDataDir))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(appDataDir, "keys")))
        .SetApplicationName("SchoolNetworkManager");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));

// Η σάρωση δικτύου στέλνει πολλά πεδία σε μία φόρμα
builder.Services.Configure<FormOptions>(options => options.ValueCountLimit = 20000);

// Υπηρεσίες δικτύου
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<OuiLookup>();
builder.Services.AddSingleton<DeviceProbe>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<MySpeedClient>();

// Pi-hole: στο τοπικό δίκτυο δεχόμαστε και αυτο-υπογεγραμμένο πιστοποιητικό (https του Pi-hole v6).
builder.Services.AddHttpClient(PiholeClient.HttpClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
builder.Services.AddSingleton<PiholeClient>();
builder.Services.AddSingleton<PublicRateLimiter>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<BackupScheduler>();
builder.Services.AddHostedService<MonitorService>();

var app = builder.Build();

// Κρυπτογράφηση κωδικών συσκευών (πριν από οποιαδήποτε χρήση της βάσης)
PasswordProtector.Initialize(app.Services.GetRequiredService<IDataProtectionProvider>());

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();

    // Νέες στήλες για βάσεις από παλιότερες εκδόσεις
    DbSchema.Upgrade(db);

    var settings = db.AppSettings.FirstOrDefault(x => x.Id == 1);

    if (settings == null)
    {
        settings = new AppSettings
        {
            Id = 1,
            SchoolTitle = "School Network Manager",
            SchoolSubtitle = "Γρήγορη εικόνα του σχολικού δικτύου"
        };

        db.AppSettings.Add(settings);
        db.SaveChanges();
    }

    app.Services.GetRequiredService<SettingsService>().Update(settings);

    // Κρυπτογράφηση κωδικών που είχαν αποθηκευτεί ως απλό κείμενο
    try
    {
        var plainPasswords = db.Computers
            .FromSqlRaw("SELECT * FROM Computers WHERE Password IS NOT NULL AND Password <> '' AND Password NOT LIKE 'enc:%'")
            .ToList();

        foreach (var computer in plainPasswords)
        {
            db.Entry(computer).Property(c => c.Password).IsModified = true;
        }

        if (plainPasswords.Count > 0)
        {
            db.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Δεν έγινε κρυπτογράφηση των υπαρχόντων κωδικών.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

// Πρώτη σύνδεση με κωδικό που έδωσε ο διαχειριστής: πρώτα αλλαγή κωδικού.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    if (context.User.Identity?.IsAuthenticated == true &&
        context.User.HasClaim(UserPasswords.ClaimMustChange, "1") &&
        !path.StartsWithSegments("/Account") &&
        !path.StartsWithSegments("/Logout") &&
        !path.StartsWithSegments("/Login") &&
        !Path.HasExtension(path.Value ?? string.Empty))
    {
        context.Response.Redirect("/Account?first=true");
        return;
    }

    await next();
});

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