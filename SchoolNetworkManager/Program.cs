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
builder.Services.AddHostedService<AutoShutdownScheduler>();

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

    // Κλειδί για τους agents των υπολογιστών (μία φορά)
    if (string.IsNullOrWhiteSpace(settings.AgentKey))
    {
        settings.AgentKey = AgentIngest.NewKey();
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

// ---------- Agent υπολογιστών ----------
// Οι agents (snm-agent.ps1) στέλνουν εδώ τα στοιχεία τους. Χωρίς σύνδεση χρήστη· με το κλειδί του agent.
app.MapPost("/api/agent/report", async (HttpContext http, AppDbContext db, SettingsService settings) =>
{
    if (!AgentIngest.KeyMatches(settings.Current.AgentKey, http.Request.Headers["X-Agent-Key"]))
    {
        return Results.Json(new { ok = false, message = "Λάθος κλειδί agent." }, statusCode: 401);
    }

    var remote = http.Connection.RemoteIpAddress?.MapToIPv4().ToString();

    if (AgentIngest.Throttle(remote))
    {
        return Results.Json(new { ok = false, message = "Πολύ συχνές αναφορές." }, statusCode: 429);
    }

    AgentIngest.Payload? payload;

    try
    {
        payload = await System.Text.Json.JsonSerializer.DeserializeAsync<AgentIngest.Payload>(http.Request.Body, AgentIngest.Json);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { ok = false, message = "Μη έγκυρα δεδομένα." }, statusCode: 400);
    }

    if (payload == null)
    {
        return Results.Json(new { ok = false, message = "Κενή αναφορά." }, statusCode: 400);
    }

    var result = await AgentIngest.ProcessAsync(db, payload, remote);

    return Results.Json(new
    {
        ok = result.Ok,
        message = result.Message,
        computerId = result.ComputerId,
        latestVersion = AgentIngest.LatestAgentVersion
    }, statusCode: result.Ok ? 200 : 422);
}).WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(4 * 1024 * 1024));

// Εκκρεμείς εντολές για έναν υπολογιστή (κλείσιμο, επανεκκίνηση, μήνυμα…). Ο agent τις ρωτάει τακτικά.
app.MapGet("/api/agent/commands", async (HttpContext http, AppDbContext db, SettingsService settings) =>
{
    if (!AgentIngest.KeyMatches(settings.Current.AgentKey, http.Request.Headers["X-Agent-Key"]))
    {
        return Results.StatusCode(401);
    }

    if (!int.TryParse(http.Request.Query["computerId"], out var computerId) || computerId <= 0)
    {
        return Results.Json(new { commands = Array.Empty<object>() });
    }

    var pending = await AgentCommands.TakePendingAsync(db, computerId);

    var cfg = settings.Current;
    var rust = cfg.RustDeskApply && !string.IsNullOrWhiteSpace(cfg.RustDeskHost)
        ? new
        {
            host = cfg.RustDeskHost,
            key = cfg.RustDeskKey,
            password = PasswordProtector.Unprotect(cfg.RustDeskPassword)
        }
        : null;

    return Results.Json(new
    {
        commands = pending.Select(c => new { id = c.Id, kind = c.Kind, text = c.Text, delay = c.DelaySeconds }),
        rustdesk = rust
    });
});

// Ο agent αναφέρει το αποτέλεσμα μιας εντολής.
app.MapPost("/api/agent/commands/result", async (HttpContext http, AppDbContext db, SettingsService settings) =>
{
    if (!AgentIngest.KeyMatches(settings.Current.AgentKey, http.Request.Headers["X-Agent-Key"]))
    {
        return Results.StatusCode(401);
    }

    CommandResult? body;
    try
    {
        body = await System.Text.Json.JsonSerializer.DeserializeAsync<CommandResult>(http.Request.Body, AgentIngest.Json);
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.BadRequest();
    }

    if (body == null || body.Id <= 0 || body.ComputerId <= 0)
    {
        return Results.BadRequest();
    }

    await AgentCommands.CompleteAsync(db, body.Id, body.ComputerId, body.Ok, body.Message);
    return Results.Json(new { ok = true });
});

// Αυτόματη ενημέρωση του agent: κατεβάζει την τελευταία έκδοση του script.
app.MapGet("/api/agent/script", (HttpContext http, SettingsService settings) =>
{
    if (!AgentIngest.KeyMatches(settings.Current.AgentKey, http.Request.Headers["X-Agent-Key"]))
    {
        return Results.StatusCode(401);
    }

    var path = Path.Combine(AppContext.BaseDirectory, "Resources", "Agent", "snm-agent.ps1");

    return File.Exists(path)
        ? Results.File(path, "text/plain; charset=utf-8", "snm-agent.ps1")
        : Results.NotFound();
});

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

/// <summary>Αποτέλεσμα εκτέλεσης μιας εντολής, όπως το στέλνει ο agent.</summary>
record CommandResult
{
    public int Id { get; init; }
    public int ComputerId { get; init; }
    public bool Ok { get; init; }
    public string? Message { get; init; }
}
