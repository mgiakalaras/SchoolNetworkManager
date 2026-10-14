using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Data;

namespace SchoolNetworkManager.Services;

/// <summary>
/// Αντίγραφα ασφαλείας: δημιουργία (χειροκίνητα / αυτόματα), λίστα, διαγραφή παλιών και επαναφορά.
/// Κάθε zip έχει: data/schoolnetworkmanager.db, data/keys/*, uploads/* (λογότυπο, κατόψεις, έγγραφα).
/// Αποθηκεύονται στο data/backups (ίδιο volume με τη βάση).
/// </summary>
public partial class BackupService
{
    public const string KindAuto = "auto";
    public const string KindManual = "manual";
    public const string KindBeforeRestore = "pre-restore";

    [GeneratedRegex(@"^snm_(\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2})_(auto|manual|pre-restore)\.zip$")]
    private static partial Regex BackupName();

    private readonly IServiceScopeFactory _scopes;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<BackupService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public BackupService(IServiceScopeFactory scopes, IWebHostEnvironment environment, ILogger<BackupService> logger)
    {
        _scopes = scopes;
        _environment = environment;
        _logger = logger;
    }

    public record BackupInfo(string Name, DateTime CreatedAt, string Kind, long Size)
    {
        public string KindLabel => Kind switch
        {
            KindAuto => "🕛 Αυτόματο",
            KindBeforeRestore => "↩️ Πριν από επαναφορά",
            _ => "💾 Χειροκίνητο"
        };
    }

    // ---------------- Διαδρομές ----------------

    private string DatabasePath()
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var source = ((SqliteConnection)db.Database.GetDbConnection()).DataSource;

        return Path.GetFullPath(string.IsNullOrWhiteSpace(source) ? "schoolnetworkmanager.db" : source);
    }

    public string DataDirectory => Path.GetDirectoryName(DatabasePath()) ?? AppContext.BaseDirectory;

    public string BackupDirectory
    {
        get
        {
            var folder = Path.Combine(DataDirectory, "backups");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    private string UploadsDirectory => Path.Combine(_environment.WebRootPath, "uploads");

    public long DatabaseSize => File.Exists(DatabasePath()) ? new FileInfo(DatabasePath()).Length : 0;

    /// <summary>Μόνο ονόματα που φτιάχνει η εφαρμογή — τίποτα άλλο δεν διαβάζεται / σβήνεται.</summary>
    public string? PathOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !BackupName().IsMatch(name))
        {
            return null;
        }

        var path = Path.Combine(BackupDirectory, name);
        return File.Exists(path) ? path : null;
    }

    public List<BackupInfo> List()
    {
        return Directory.GetFiles(BackupDirectory, "snm_*.zip")
            .Select(path => (path, match: BackupName().Match(Path.GetFileName(path))))
            .Where(x => x.match.Success)
            .Select(x => new BackupInfo(
                Path.GetFileName(x.path),
                DateTime.ParseExact(x.match.Groups[1].Value, "yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture),
                x.match.Groups[2].Value,
                new FileInfo(x.path).Length))
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    // ---------------- Δημιουργία ----------------

    public async Task<BackupInfo> CreateAsync(string kind)
    {
        await _lock.WaitAsync();

        try
        {
            var now = DateTime.Now;
            var name = $"snm_{now:yyyy-MM-dd_HH-mm-ss}_{kind}.zip";
            var target = Path.Combine(BackupDirectory, name);
            var temp = target + ".tmp";

            await using (var stream = new FileStream(temp, FileMode.Create))
            {
                await WriteZipAsync(stream);
            }

            File.Move(temp, target, overwrite: true);
            _logger.LogInformation("Αντίγραφο ασφαλείας: {Name}", name);

            return new BackupInfo(name, now, kind, new FileInfo(target).Length);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Γράφει το zip σε οποιοδήποτε stream (και για τη «Λήψη τώρα»).</summary>
    public async Task WriteZipAsync(Stream output)
    {
        var databasePath = DatabasePath();
        var tempDb = Path.Combine(Path.GetTempPath(), $"snm_backup_{Guid.NewGuid():N}.db");

        try
        {
            // Ασφαλές αντίγραφο της βάσης ακόμα κι όσο η εφαρμογή τη χρησιμοποιεί
            await using (var source = new SqliteConnection($"Data Source={databasePath}"))
            await using (var copy = new SqliteConnection($"Data Source={tempDb};Pooling=False"))
            {
                await source.OpenAsync();
                await copy.OpenAsync();
                source.BackupDatabase(copy);
            }

            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

            zip.CreateEntryFromFile(tempDb, "data/schoolnetworkmanager.db", CompressionLevel.Optimal);

            var keys = Path.Combine(DataDirectory, "keys");

            if (Directory.Exists(keys))
            {
                foreach (var file in Directory.GetFiles(keys))
                {
                    zip.CreateEntryFromFile(file, "data/keys/" + Path.GetFileName(file));
                }
            }

            if (Directory.Exists(UploadsDirectory))
            {
                foreach (var file in Directory.GetFiles(UploadsDirectory, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(UploadsDirectory, file).Replace('\\', '/');
                    zip.CreateEntryFromFile(file, "uploads/" + relative, CompressionLevel.Fastest);
                }
            }
        }
        finally
        {
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { /* προσωρινό */ }
        }
    }

    /// <summary>Κρατάει τα τελευταία N αυτόματα (και τα 3 τελευταία «πριν από επαναφορά»).</summary>
    public int Prune(int keepAuto)
    {
        var removed = 0;
        var all = List();

        foreach (var old in all.Where(b => b.Kind == KindAuto).Skip(Math.Max(1, keepAuto))
                     .Concat(all.Where(b => b.Kind == KindBeforeRestore).Skip(3)))
        {
            try
            {
                File.Delete(Path.Combine(BackupDirectory, old.Name));
                removed++;
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Δεν σβήστηκε το {Name}", old.Name);
            }
        }

        return removed;
    }

    public bool Delete(string name)
    {
        var path = PathOf(name);

        if (path == null)
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    // ---------------- Επαναφορά ----------------

    /// <summary>
    /// Επαναφέρει βάση, κλειδιά και αρχεία από zip. Πριν από αυτό φτιάχνει αντίγραφο της τωρινής κατάστασης.
    /// Η βάση αντικαθίσταται «ζωντανά» με το SQLite backup API — δεν χρειάζεται επανεκκίνηση.
    /// </summary>
    public async Task<(bool Ok, string Message)> RestoreAsync(Stream zipStream)
    {
        var work = Path.Combine(Path.GetTempPath(), $"snm_restore_{Guid.NewGuid():N}");
        Directory.CreateDirectory(work);

        try
        {
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                var dbEntry = zip.GetEntry("data/schoolnetworkmanager.db");

                if (dbEntry == null)
                {
                    return (false, "Το zip δεν είναι αντίγραφο της εφαρμογής (λείπει το data/schoolnetworkmanager.db).");
                }

                foreach (var entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    var destination = Path.GetFullPath(Path.Combine(work, entry.FullName));

                    // Προστασία από «../» μέσα στο zip
                    if (!destination.StartsWith(Path.GetFullPath(work) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            var restoredDb = Path.Combine(work, "data", "schoolnetworkmanager.db");

            // Έλεγχος ότι είναι έγκυρη βάση της εφαρμογής
            await using (var check = new SqliteConnection($"Data Source={restoredDb};Mode=ReadOnly;Pooling=False"))
            {
                await check.OpenAsync();
                await using var command = check.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Computers','AppSettings')";
                var tables = Convert.ToInt32(await command.ExecuteScalarAsync());

                if (tables < 2)
                {
                    return (false, "Η βάση μέσα στο zip δεν είναι της εφαρμογής.");
                }
            }

            // Πρώτα: αντίγραφο της τωρινής κατάστασης, για να μπορεί να αναιρεθεί η επαναφορά
            var safety = await CreateAsync(KindBeforeRestore);

            await _lock.WaitAsync();

            try
            {
                SqliteConnection.ClearAllPools();

                await using (var source = new SqliteConnection($"Data Source={restoredDb};Pooling=False"))
                await using (var live = new SqliteConnection($"Data Source={DatabasePath()};Pooling=False"))
                {
                    await source.OpenAsync();
                    await live.OpenAsync();
                    source.BackupDatabase(live);
                }

                SqliteConnection.ClearAllPools();

                // Κλειδιά: προσθέτουμε όσα λείπουν (δεν σβήνουμε τα τωρινά)
                var keysFrom = Path.Combine(work, "data", "keys");
                var keysTo = Path.Combine(DataDirectory, "keys");

                if (Directory.Exists(keysFrom))
                {
                    Directory.CreateDirectory(keysTo);

                    foreach (var file in Directory.GetFiles(keysFrom))
                    {
                        var target = Path.Combine(keysTo, Path.GetFileName(file));

                        if (!File.Exists(target))
                        {
                            File.Copy(file, target);
                        }
                    }
                }

                // Αρχεία (λογότυπο, κατόψεις, έγγραφα)
                var uploadsFrom = Path.Combine(work, "uploads");

                if (Directory.Exists(uploadsFrom))
                {
                    foreach (var file in Directory.GetFiles(uploadsFrom, "*", SearchOption.AllDirectories))
                    {
                        var target = Path.Combine(UploadsDirectory, Path.GetRelativePath(uploadsFrom, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target, overwrite: true);
                    }
                }
            }
            finally
            {
                _lock.Release();
            }

            // Παλιότερο αντίγραφο → νέες στήλες / πίνακες, και ανανέωση ρυθμίσεων
            using (var scope = _scopes.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                DbSchema.Upgrade(db);

                var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == 1);

                if (settings != null)
                {
                    scope.ServiceProvider.GetRequiredService<SettingsService>().Update(settings);
                }
            }

            return (true, $"Η επαναφορά ολοκληρώθηκε. Η προηγούμενη κατάσταση φυλάχτηκε ως «{safety.Name}».");
        }
        catch (InvalidDataException)
        {
            return (false, "Το αρχείο δεν είναι έγκυρο zip.");
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "Αποτυχία επαναφοράς");
            return (false, "Η βάση μέσα στο zip δεν διαβάζεται: " + ex.Message);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch { /* προσωρινό */ }
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB"
    };
}

/// <summary>Κάθε μέρα στην ώρα που ορίζουν οι Ρυθμίσεις: αυτόματο αντίγραφο + σβήσιμο των παλιών.</summary>
public class BackupScheduler : BackgroundService
{
    private readonly BackupService _backups;
    private readonly SettingsService _settings;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(BackupService backups, SettingsService settings, ILogger<BackupScheduler> logger)
    {
        _backups = backups;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Λίγο μετά την εκκίνηση, για να έχει ετοιμαστεί η βάση
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _settings.Current;

                if (settings.AutoBackup)
                {
                    var now = DateTime.Now;
                    var lastAuto = _backups.List().FirstOrDefault(b => b.Kind == BackupService.KindAuto);
                    var dueToday = now.Hour >= settings.AutoBackupHour && (lastAuto == null || lastAuto.CreatedAt.Date < now.Date);

                    // Αν ο server ήταν κλειστός την ώρα του αντιγράφου, το φτιάχνει μόλις ανοίξει.
                    var missed = lastAuto == null || now - lastAuto.CreatedAt > TimeSpan.FromHours(30);

                    if (dueToday || missed)
                    {
                        await _backups.CreateAsync(BackupService.KindAuto);
                        _backups.Prune(settings.BackupKeep);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Το αυτόματο αντίγραφο απέτυχε");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
