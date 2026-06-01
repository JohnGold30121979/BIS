using Microsoft.EntityFrameworkCore;
using BIS.ERP.Models;
using BIS.ERP.Services;

namespace BIS.ERP.Data;

public class AppDbContext : DbContext
{
    private readonly string _connectionString;

    public AppDbContext() : this(AppSettings.Instance.GetMasterConnectionString())
    {
    }

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<InfoBase> InfoBases { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<Employee> Employees { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Уникальные индексы
        modelBuilder.Entity<InfoBase>()
            .HasIndex(x => x.Name)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(x => x.Login)
            .IsUnique();

        modelBuilder.Entity<Material>()
            .HasIndex(x => x.Code)
            .IsUnique();

        modelBuilder.Entity<FixedAsset>()
            .HasIndex(x => x.InventoryNumber)
            .IsUnique();

        modelBuilder.Entity<Employee>()
            .HasIndex(x => x.PersonnelNumber)
            .IsUnique();
    }

    public static string BuildConnectionString(string host, int port, string database, string username, string password)
    {
        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
}