// Data/AppDbContext.cs
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

    // DbSet для существующих моделей
    public DbSet<InfoBase> InfoBases { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<Employee> Employees { get; set; }

    // DbSet для метаданных
    public DbSet<MetadataObject> MetadataObjects { get; set; }
    public DbSet<MetadataField> MetadataFields { get; set; }
    public DbSet<MetadataConfiguration> MetadataConfigurations { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql(_connectionString);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Уникальные индексы для существующих моделей
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

        // Настройка для MetadataObject
        modelBuilder.Entity<MetadataObject>()
            .HasKey(m => m.Id);

        modelBuilder.Entity<MetadataObject>()
            .Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(100);

        modelBuilder.Entity<MetadataObject>()
            .HasMany(m => m.Fields)
            .WithOne(f => f.MetadataObject)
            .HasForeignKey(f => f.MetadataObjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Настройка для MetadataField
        modelBuilder.Entity<MetadataField>()
            .HasKey(f => f.Id);

        // Настройка для MetadataConfiguration
        modelBuilder.Entity<MetadataConfiguration>()
            .HasKey(c => c.Id);
    }

    public static string BuildConnectionString(string host, int port, string database, string username, string password)
    {
        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
}