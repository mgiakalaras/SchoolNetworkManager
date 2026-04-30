using Microsoft.EntityFrameworkCore;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Computer> Computers => Set<Computer>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Computer>()
            .HasIndex(c => c.IpAddress)
            .IsUnique();

        modelBuilder.Entity<Computer>()
            .HasIndex(c => c.MacAddress);
    }
}