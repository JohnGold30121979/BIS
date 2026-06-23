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
    public DbSet<MetadataObject> MetadataObjects { get; set; }
    public DbSet<MetadataField> MetadataFields { get; set; }
    public DbSet<MetadataConfiguration> MetadataConfigurations { get; set; }

    // Новые DbSet для отчетов
    public DbSet<Report> Reports { get; set; }
    public DbSet<ReportField> ReportFields { get; set; }
    public DbSet<ReportFilter> ReportFilters { get; set; }
    public DbSet<ReportGroup> ReportGroups { get; set; }

    // DbSet для документов (без внешних ключей на InfoBase)
    public DbSet<Document> Documents { get; set; }
    public DbSet<DocumentMovement> DocumentMovements { get; set; }
    public DbSet<DocumentRow> DocumentRows { get; set; }

    public DbSet<DynamicDocument> DynamicDocuments { get; set; }
    public DbSet<DynamicDocumentRow> DynamicDocumentRows { get; set; }
    public DbSet<DbfMetadata> DbfMetadata { get; set; }
   
    public DbSet<MetadataCalculation> MetadataCalculations { get; set; }
    public DbSet<MetadataPostingRule> MetadataPostingRules { get; set; }
    public DbSet<Organization> Organizations { get; set; }   
    public DbSet<Posting> Postings { get; set; }  
    public DbSet<AccountingPeriod> AccountingPeriods { get; set; }
    public DbSet<AccountOpeningBalance> AccountOpeningBalances { get; set; }
    public DbSet<AccountTurnoverSnapshot> AccountTurnoverSnapshots { get; set; }
    public DbSet<FinancialReportLine> FinancialReportLines { get; set; }
    public DbSet<FinancialReportLineAccount> FinancialReportLineAccounts { get; set; }
    public DbSet<TaxJournalRecord> TaxJournalRecords { get; set; }
    public DbSet<LocalizationEntry> LocalizationEntries { get; set; }
    public DbSet<SystemConfiguration> SystemConfigurations { get; set; }
    public DbSet<UserAccessPermission> UserAccessPermissions { get; set; }
    public DbSet<MetadataModule> MetadataModules { get; set; }
    public DbSet<MetadataModuleItem> MetadataModuleItems { get; set; }
    

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
        modelBuilder.Entity<UserAccessPermission>().HasKey(permission => permission.Id);
        modelBuilder.Entity<UserAccessPermission>()
            .HasIndex(permission => new { permission.UserId, permission.NavigationKey })
            .IsUnique();
        modelBuilder.Entity<MetadataModule>().HasKey(module => module.Id);
        modelBuilder.Entity<MetadataModule>().HasIndex(module => module.Code).IsUnique();
        modelBuilder.Entity<MetadataModuleItem>().HasKey(item => item.Id);
        modelBuilder.Entity<MetadataModuleItem>()
            .HasIndex(item => new { item.ObjectType, item.ObjectId }).IsUnique();
        modelBuilder.Entity<Material>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<FixedAsset>().HasIndex(x => x.InventoryNumber).IsUnique();       

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

        // НАСТРОЙКИ ДЛЯ ДОКУМЕНТОВ (без связи с InfoBase)
        modelBuilder.Entity<Document>().HasKey(d => d.Id);
        modelBuilder.Entity<Document>().Property(d => d.Number).HasMaxLength(50);
        modelBuilder.Entity<Document>().Property(d => d.DocumentType).HasMaxLength(20);
        modelBuilder.Entity<Document>().Property(d => d.OperationCode).HasMaxLength(50);
        modelBuilder.Entity<Document>().Property(d => d.OperationDescription).HasMaxLength(500);
        // Индексы для документов
        modelBuilder.Entity<Document>().HasIndex(d => d.Number);
        modelBuilder.Entity<Document>().HasIndex(d => d.Date);

        // Настройки для движений документов
        modelBuilder.Entity<DocumentMovement>().HasKey(m => m.Id);
        modelBuilder.Entity<DocumentMovement>()
            .HasOne(m => m.Document)
            .WithMany()
            .HasForeignKey(m => m.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Настройки для документов и строк
        modelBuilder.Entity<Document>()
            .HasMany(d => d.Rows)
            .WithOne(r => r.Document)
            .HasForeignKey(r => r.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);      

        modelBuilder.Entity<DocumentRow>()
            .HasKey(r => r.Id);

        // Настройки для DynamicDocument
        modelBuilder.Entity<DynamicDocument>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Number).HasMaxLength(50).IsRequired();
            entity.Property(e => e.Date).IsRequired();
            entity.Property(e => e.DocumentType).HasMaxLength(50);
            entity.Property(e => e.SourceFile).HasMaxLength(200);
            entity.Property(e => e.TotalRows).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasMany(e => e.Rows)
                  .WithOne(e => e.Document)
                  .HasForeignKey(e => e.DocumentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Настройки для DynamicDocumentRow
        modelBuilder.Entity<DynamicDocumentRow>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DocumentId).IsRequired();
            entity.Property(e => e.RowNumber).IsRequired();
            entity.Property(e => e.Data)
                  .HasColumnType("jsonb")
                  .IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        // Настройки для DbfMetadata
        modelBuilder.Entity<DbfMetadata>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Fields)
                  .HasColumnType("jsonb")
                  .IsRequired();
            entity.Property(e => e.TotalRecords).IsRequired();
            entity.Property(e => e.ImportedAt).IsRequired();
        });

        modelBuilder.Entity<AccountingPeriod>().HasIndex(period => new { period.StartDate, period.EndDate }).IsUnique();
        modelBuilder.Entity<AccountOpeningBalance>().HasIndex(balance => new { balance.BalanceDate, balance.AccountCode }).IsUnique();
        modelBuilder.Entity<AccountTurnoverSnapshot>().HasIndex(snapshot => new { snapshot.PeriodId, snapshot.AccountCode }).IsUnique();
        modelBuilder.Entity<FinancialReportLine>().HasIndex(line => new { line.ReportCode, line.LineCode }).IsUnique();
        modelBuilder.Entity<FinancialReportLineAccount>().HasIndex(link => new { link.LineId, link.AccountCode }).IsUnique();
        modelBuilder.Entity<LocalizationEntry>().HasIndex(entry => new { entry.Culture, entry.Key }).IsUnique();
    }



    public static string BuildConnectionString(string host, int port, string database, string username, string password)
    {
        return $"Host={host};Port={port};Database={database};Username={username};Password={password}";
    }
}
