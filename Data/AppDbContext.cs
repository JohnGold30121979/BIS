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

    // Существующие DbSet
    public DbSet<InfoBase> InfoBases { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Transaction> Transactions { get; set; }
    public DbSet<Material> Materials { get; set; }
    public DbSet<FixedAsset> FixedAssets { get; set; }
    public DbSet<Employee> Employees { get; set; }
    public DbSet<MetadataObject> MetadataObjects { get; set; }
    public DbSet<MetadataField> MetadataFields { get; set; }
    public DbSet<MetadataConfiguration> MetadataConfigurations { get; set; }

    // Новые DbSet для отчетов
    public DbSet<Report> Reports { get; set; }
    public DbSet<ReportField> ReportFields { get; set; }
    public DbSet<ReportFilter> ReportFilters { get; set; }
    public DbSet<ReportGroup> ReportGroups { get; set; }

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

        // Существующие настройки...
        modelBuilder.Entity<InfoBase>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<User>().HasIndex(x => x.Login).IsUnique();
        modelBuilder.Entity<Material>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<FixedAsset>().HasIndex(x => x.InventoryNumber).IsUnique();
        modelBuilder.Entity<Employee>().HasIndex(x => x.PersonnelNumber).IsUnique();

        // Настройки для метаданных
        modelBuilder.Entity<MetadataObject>().HasKey(m => m.Id);
        modelBuilder.Entity<MetadataObject>().HasMany(m => m.Fields).WithOne(f => f.MetadataObject).HasForeignKey(f => f.MetadataObjectId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<MetadataField>().HasKey(f => f.Id);
        modelBuilder.Entity<MetadataConfiguration>().HasKey(c => c.Id);

        // Настройки для отчетов
        modelBuilder.Entity<Report>().HasKey(r => r.Id);
        modelBuilder.Entity<Report>().HasMany(r => r.Fields).WithOne(f => f.Report).HasForeignKey(f => f.ReportId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Report>().HasMany(r => r.Filters).WithOne(f => f.Report).HasForeignKey(f => f.ReportId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Report>().HasMany(r => r.Groups).WithOne(g => g.Report).HasForeignKey(g => g.ReportId).OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ReportField>().HasKey(f => f.Id);
        modelBuilder.Entity<ReportFilter>().HasKey(f => f.Id);
        modelBuilder.Entity<ReportGroup>().HasKey(g => g.Id);
    }

    public static string BuildConnectionString(string host, int port, string database, string username, string password)
    {
        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
}