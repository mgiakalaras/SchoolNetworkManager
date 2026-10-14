using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Models;
using SchoolNetworkManager.Services;

namespace SchoolNetworkManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<StatusSnapshot> StatusSnapshots => Set<StatusSnapshot>();
    public DbSet<DeviceHour> DeviceHours => Set<DeviceHour>();
    public DbSet<Fault> Faults => Set<Fault>();
    public DbSet<Consumable> Consumables => Set<Consumable>();
    public DbSet<ConsumableLog> ConsumableLogs => Set<ConsumableLog>();
    public DbSet<PlanItem> PlanItems => Set<PlanItem>();
    public DbSet<RoomPlan> RoomPlans => Set<RoomPlan>();
    public DbSet<SchoolClass> SchoolClasses => Set<SchoolClass>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<PasswordRecord> PasswordRecords => Set<PasswordRecord>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SchoolDocument> Documents => Set<SchoolDocument>();
    public DbSet<FaultReport> FaultReports => Set<FaultReport>();
    public DbSet<Room> Rooms => Set<Room>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Computer>()
            .HasIndex(c => c.IpAddress)
            .IsUnique();

        modelBuilder.Entity<Computer>()
            .HasIndex(c => c.MacAddress);

        modelBuilder.Entity<AppUser>()
            .HasIndex(u => u.UserName)
            .IsUnique();

        modelBuilder.Entity<AppUser>()
            .Ignore(u => u.Initials);

        modelBuilder.Entity<SchoolDocument>()
            .HasIndex(d => d.Key)
            .IsUnique();

        modelBuilder.Entity<SchoolDocument>()
            .Ignore(d => d.Icon);

        modelBuilder.Entity<FaultReport>()
            .HasIndex(r => r.Status);

        modelBuilder.Entity<FaultReport>()
            .Ignore(r => r.ReporterLabel);

        modelBuilder.Entity<DeviceHour>()
            .HasIndex(h => new { h.ComputerId, h.Hour })
            .IsUnique();

        modelBuilder.Entity<Fault>()
            .HasIndex(f => new { f.ComputerId, f.ResolvedAt });

        modelBuilder.Entity<Fault>()
            .Ignore(f => f.IsOpen)
            .Ignore(f => f.IsDown);

        modelBuilder.Entity<Consumable>()
            .Ignore(c => c.IsLow)
            .Ignore(c => c.IsOut);

        // Οι κωδικοί του ιστορικού αποθηκεύονται κρυπτογραφημένοι.
        modelBuilder.Entity<PasswordRecord>()
            .Property(p => p.Secret)
            .HasConversion(
                v => PasswordProtector.Protect(v),
                v => PasswordProtector.Unprotect(v));

        // Οι κωδικοί των συσκευών αποθηκεύονται κρυπτογραφημένοι.
        modelBuilder.Entity<Computer>()
            .Property(c => c.Password)
            .HasConversion(
                v => PasswordProtector.Protect(v),
                v => PasswordProtector.Unprotect(v));
    }
}