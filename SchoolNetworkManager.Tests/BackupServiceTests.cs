using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolNetworkManager.Data;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Tests;

/// <summary>BackupService πάνω σε πραγματικό αρχείο SQLite σε προσωρινό φάκελο.</summary>
public class BackupServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "snm_test_" + Guid.NewGuid().ToString("N"));
    private readonly ServiceProvider _services;
    private readonly BackupService _backups;

    private sealed class FakeEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.Combine(root, "wwwroot");
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Development";
    }

    public BackupServiceTests()
    {
        TestDb.CreateWithoutUpgrade().Conn.Dispose(); // αρχικοποιεί το PasswordProtector
        Directory.CreateDirectory(_dir);
        var dbPath = Path.Combine(_dir, "schoolnetworkmanager.db");

        _services = new ServiceCollection()
            .AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={dbPath}"))
            .AddSingleton<SettingsService>()
            .BuildServiceProvider();

        using (var scope = _services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            DbSchema.Upgrade(db);
            db.AppSettings.Add(new AppSettings { Id = 1, SchoolTitle = "Test" });
            db.Computers.Add(new Computer { Name = "PC1", IpAddress = "10.9.0.1", Location = "Αίθουσα 3" });
            db.SaveChanges();
        }

        _backups = new BackupService(_services.GetRequiredService<IServiceScopeFactory>(), new FakeEnvironment(_dir), NullLogger<BackupService>.Instance);
    }

    public void Dispose()
    {
        _services.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* προσωρινό */ }
    }

    private async Task ConvertAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await RoomMigration.RunAsync(db, () => Task.CompletedTask, NullLogger.Instance));
    }

    [Fact]
    public async Task Restore_OfUnconvertedBackup_RunsRoomConversion()
    {
        var old = await _backups.CreateAsync(BackupService.KindManual);
        await ConvertAsync();

        await using (var zip = File.OpenRead(_backups.PathOf(old.Name)!))
        {
            var (ok, _) = await _backups.RestoreAsync(zip);
            Assert.True(ok);
        }

        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var room = await db.Rooms.SingleAsync();
        var pc = await db.Computers.SingleAsync();
        Assert.Equal(room.Id, pc.RoomId);
        Assert.Equal("Αίθουσα 3", pc.Location);
        Assert.NotNull((await db.AppSettings.SingleAsync()).RoomsMigratedAt);
        Assert.NotNull(_services.GetRequiredService<SettingsService>().Current.RoomsMigratedAt);
    }

    [Fact]
    public async Task BeforeRoomsBackup_IsCreatedOncePerDay()
    {
        await _backups.CreateBeforeRoomsAsync();
        await Task.Delay(1100); // διαφορετικό όνομα αρχείου (δευτερόλεπτα)
        await _backups.CreateBeforeRoomsAsync();

        Assert.Single(_backups.List(), b => b.Kind == BackupService.KindBeforeRooms);
    }
}
