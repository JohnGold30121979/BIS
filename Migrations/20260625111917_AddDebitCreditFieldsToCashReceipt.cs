using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BIS.ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddDebitCreditFieldsToCashReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountingPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountingPeriods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountOpeningBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BalanceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Debit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountOpeningBalances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountTurnoverSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    OpeningDebit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    OpeningCredit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TurnoverDebit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TurnoverCredit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingDebit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ClosingCredit = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTurnoverSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DbfMetadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Fields = table.Column<string>(type: "jsonb", nullable: false),
                    TotalRecords = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DbfMetadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsPosted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DynamicDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SourceFile = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalRows = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Employee",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonnelNumber = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    Department = table.Column<string>(type: "text", nullable: false),
                    HireDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TerminationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    TaxId = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employee", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialReportLineAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LineId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialReportLineAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FinancialReportLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    LineCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SectionCode = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Sign = table.Column<int>(type: "integer", nullable: false),
                    IsTotal = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialReportLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InfoBases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Host = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    DatabaseName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Username = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Password = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfoBases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocalizationEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Culture = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocalizationEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InfoBaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    IsInitialized = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataModuleItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataModuleItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MetadataModules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Icon = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataModules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TaxId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    RegistrationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Okpo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LegalAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActualAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Website = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Director = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChiefAccountant = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DataSourceType = table.Column<string>(type: "text", nullable: false),
                    DataSourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportType = table.Column<string>(type: "text", nullable: false),
                    Template = table.Column<string>(type: "text", nullable: false),
                    Settings = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsPrintForm = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    SourceFormat = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TemplateVersion = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PageTitle = table.Column<string>(type: "text", nullable: false),
                    PageOrientation = table.Column<string>(type: "text", nullable: false),
                    PageWidth = table.Column<int>(type: "integer", nullable: false),
                    PageHeight = table.Column<int>(type: "integer", nullable: false),
                    LeftMargin = table.Column<int>(type: "integer", nullable: false),
                    RightMargin = table.Column<int>(type: "integer", nullable: false),
                    TopMargin = table.Column<int>(type: "integer", nullable: false),
                    BottomMargin = table.Column<int>(type: "integer", nullable: false),
                    FontName = table.Column<string>(type: "text", nullable: false),
                    FontSize = table.Column<int>(type: "integer", nullable: false),
                    ShowHeader = table.Column<bool>(type: "boolean", nullable: false),
                    ShowFooter = table.Column<bool>(type: "boolean", nullable: false),
                    ShowPageNumbers = table.Column<bool>(type: "boolean", nullable: false),
                    ShowGridLines = table.Column<bool>(type: "boolean", nullable: false),
                    AlternateRowColor = table.Column<string>(type: "text", nullable: false),
                    HeaderTitle = table.Column<string>(type: "text", nullable: false),
                    HeaderSubtitle = table.Column<string>(type: "text", nullable: false),
                    HeaderLogo = table.Column<string>(type: "text", nullable: false),
                    HeaderText = table.Column<string>(type: "text", nullable: false),
                    FooterText = table.Column<string>(type: "text", nullable: false),
                    FooterTotalText = table.Column<string>(type: "text", nullable: false),
                    FooterSignature = table.Column<string>(type: "text", nullable: false),
                    TitleText = table.Column<string>(type: "text", nullable: false),
                    SubtitleText = table.Column<string>(type: "text", nullable: false),
                    SummaryText = table.Column<string>(type: "text", nullable: false),
                    AlternateRowColors = table.Column<bool>(type: "boolean", nullable: false),
                    ShowGrandTotal = table.Column<bool>(type: "boolean", nullable: false),
                    HeaderColor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SystemName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Icon = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CompanyDetails = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Phone = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaxJournalRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Organization = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    TaxType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AmountWithoutTax = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TaxAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    SourceRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaxJournalRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccessPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    NavigationKey = table.Column<string>(type: "text", nullable: false),
                    IsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccessPermissions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Login = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastInfoBaseId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Debit = table.Column<decimal>(type: "numeric", nullable: false),
                    Credit = table.Column<decimal>(type: "numeric", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    MovementDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentMovements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LineNumber = table.Column<int>(type: "integer", nullable: false),
                    OperationCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    OperationDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentRows_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DynamicDocumentRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    Data = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DynamicDocumentRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DynamicDocumentRows_DynamicDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "DynamicDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FixedAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ResponsiblePerson = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AcquisitionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InitialCost = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    ResidualValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    DepreciationRate = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InfoBaseId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FixedAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssets_InfoBases_InfoBaseId",
                        column: x => x.InfoBaseId,
                        principalTable: "InfoBases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Warehouse = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MinStock = table.Column<decimal>(type: "numeric(18,3)", nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    InfoBaseId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Materials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Materials_InfoBases_InfoBaseId",
                        column: x => x.InfoBaseId,
                        principalTable: "InfoBases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InfoBaseId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_InfoBases_InfoBaseId",
                        column: x => x.InfoBaseId,
                        principalTable: "InfoBases",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "MetadataObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TableName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ObjectType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataConfigId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsePostings = table.Column<bool>(type: "boolean", nullable: false),
                    UseBalances = table.Column<bool>(type: "boolean", nullable: false),
                    UseMovements = table.Column<bool>(type: "boolean", nullable: false),
                    BalanceTable = table.Column<string>(type: "text", nullable: true),
                    MovementTable = table.Column<string>(type: "text", nullable: true),
                    ReferenceFields = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataObjects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataObjects_MetadataConfigurations_MetadataConfigId",
                        column: x => x.MetadataConfigId,
                        principalTable: "MetadataConfigurations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Postings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DocumentNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DebitAccount = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreditAccount = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    AmountCurrency = table.Column<decimal>(type: "numeric(15,2)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Postings_Employee_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employee",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Postings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReportFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    AggregateType = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Alignment = table.Column<string>(type: "text", nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    IsVisible = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportFields_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    Operation = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    Value2 = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFilters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportFilters_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    FieldName = table.Column<string>(type: "text", nullable: false),
                    Header = table.Column<string>(type: "text", nullable: false),
                    Footer = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    ShowHeader = table.Column<bool>(type: "boolean", nullable: false),
                    ShowFooter = table.Column<bool>(type: "boolean", nullable: false),
                    PageBreak = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportGroups_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportHeaderFooter",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionType = table.Column<string>(type: "text", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Alignment = table.Column<string>(type: "text", nullable: false),
                    FontName = table.Column<string>(type: "text", nullable: false),
                    FontSize = table.Column<int>(type: "integer", nullable: false),
                    IsBold = table.Column<bool>(type: "boolean", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportHeaderFooter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportHeaderFooter_Reports_ReportId",
                        column: x => x.ReportId,
                        principalTable: "Reports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataCalculations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetField = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CalculationType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Formula = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SourceFields = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsAuto = table.Column<bool>(type: "boolean", nullable: false),
                    ExecutionOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataCalculations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataCalculations_MetadataObjects_MetadataObjectId",
                        column: x => x.MetadataObjectId,
                        principalTable: "MetadataObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DbColumnName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FieldType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Length = table.Column<int>(type: "integer", nullable: false),
                    Precision = table.Column<int>(type: "integer", nullable: false),
                    Scale = table.Column<int>(type: "integer", nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    IsUnique = table.Column<bool>(type: "boolean", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    MetadataObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceCatalog = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Formula = table.Column<string>(type: "text", nullable: true),
                    DisplayPattern = table.Column<string>(type: "text", nullable: true),
                    DisplayFields = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataFields_MetadataObjects_MetadataObjectId",
                        column: x => x.MetadataObjectId,
                        principalTable: "MetadataObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MetadataPostingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MetadataObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DebitAccountExpression = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreditAccountExpression = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AmountExpression = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Condition = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetadataPostingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetadataPostingRules_MetadataObjects_MetadataObjectId",
                        column: x => x.MetadataObjectId,
                        principalTable: "MetadataObjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountingPeriods_StartDate_EndDate",
                table: "AccountingPeriods",
                columns: new[] { "StartDate", "EndDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountOpeningBalances_BalanceDate_AccountCode",
                table: "AccountOpeningBalances",
                columns: new[] { "BalanceDate", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTurnoverSnapshots_PeriodId_AccountCode",
                table: "AccountTurnoverSnapshots",
                columns: new[] { "PeriodId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentMovements_DocumentId",
                table: "DocumentMovements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentRows_DocumentId",
                table: "DocumentRows",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Date",
                table: "Documents",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Number",
                table: "Documents",
                column: "Number");

            migrationBuilder.CreateIndex(
                name: "IX_DynamicDocumentRows_DocumentId",
                table: "DynamicDocumentRows",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReportLineAccounts_LineId_AccountCode",
                table: "FinancialReportLineAccounts",
                columns: new[] { "LineId", "AccountCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialReportLines_ReportCode_LineCode",
                table: "FinancialReportLines",
                columns: new[] { "ReportCode", "LineCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_InfoBaseId",
                table: "FixedAssets",
                column: "InfoBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_InventoryNumber",
                table: "FixedAssets",
                column: "InventoryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InfoBases_Name",
                table: "InfoBases",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocalizationEntries_Culture_Key",
                table: "LocalizationEntries",
                columns: new[] { "Culture", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_Code",
                table: "Materials",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Materials_InfoBaseId",
                table: "Materials",
                column: "InfoBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataCalculations_MetadataObjectId",
                table: "MetadataCalculations",
                column: "MetadataObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataFields_MetadataObjectId",
                table: "MetadataFields",
                column: "MetadataObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataModuleItems_ObjectType_ObjectId",
                table: "MetadataModuleItems",
                columns: new[] { "ObjectType", "ObjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataModules_Code",
                table: "MetadataModules",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MetadataObjects_MetadataConfigId",
                table: "MetadataObjects",
                column: "MetadataConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_MetadataPostingRules_MetadataObjectId",
                table: "MetadataPostingRules",
                column: "MetadataObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Postings_EmployeeId",
                table: "Postings",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Postings_OrganizationId",
                table: "Postings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportFields_ReportId",
                table: "ReportFields",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportFilters_ReportId",
                table: "ReportFilters",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportGroups_ReportId",
                table: "ReportGroups",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportHeaderFooter_ReportId",
                table: "ReportHeaderFooter",
                column: "ReportId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_InfoBaseId",
                table: "Transactions",
                column: "InfoBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccessPermissions_UserId_NavigationKey",
                table: "UserAccessPermissions",
                columns: new[] { "UserId", "NavigationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Login",
                table: "Users",
                column: "Login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountingPeriods");

            migrationBuilder.DropTable(
                name: "AccountOpeningBalances");

            migrationBuilder.DropTable(
                name: "AccountTurnoverSnapshots");

            migrationBuilder.DropTable(
                name: "DbfMetadata");

            migrationBuilder.DropTable(
                name: "DocumentMovements");

            migrationBuilder.DropTable(
                name: "DocumentRows");

            migrationBuilder.DropTable(
                name: "DynamicDocumentRows");

            migrationBuilder.DropTable(
                name: "FinancialReportLineAccounts");

            migrationBuilder.DropTable(
                name: "FinancialReportLines");

            migrationBuilder.DropTable(
                name: "FixedAssets");

            migrationBuilder.DropTable(
                name: "LocalizationEntries");

            migrationBuilder.DropTable(
                name: "Materials");

            migrationBuilder.DropTable(
                name: "MetadataCalculations");

            migrationBuilder.DropTable(
                name: "MetadataFields");

            migrationBuilder.DropTable(
                name: "MetadataModuleItems");

            migrationBuilder.DropTable(
                name: "MetadataModules");

            migrationBuilder.DropTable(
                name: "MetadataPostingRules");

            migrationBuilder.DropTable(
                name: "Postings");

            migrationBuilder.DropTable(
                name: "ReportFields");

            migrationBuilder.DropTable(
                name: "ReportFilters");

            migrationBuilder.DropTable(
                name: "ReportGroups");

            migrationBuilder.DropTable(
                name: "ReportHeaderFooter");

            migrationBuilder.DropTable(
                name: "SystemConfigurations");

            migrationBuilder.DropTable(
                name: "TaxJournalRecords");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "UserAccessPermissions");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "DynamicDocuments");

            migrationBuilder.DropTable(
                name: "MetadataObjects");

            migrationBuilder.DropTable(
                name: "Employee");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Reports");

            migrationBuilder.DropTable(
                name: "InfoBases");

            migrationBuilder.DropTable(
                name: "MetadataConfigurations");
        }
    }
}
