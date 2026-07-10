using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;

namespace BIS.ERP.Services
{
    public sealed class RuntimeSchemaFixService
    {
        private readonly AppDbContext _context;
        private static readonly object SyncLock = new();
        private static readonly HashSet<string> EnsuredConnections = new(StringComparer.OrdinalIgnoreCase);

        public RuntimeSchemaFixService(AppDbContext context)
        {
            _context = context;
        }

        public async Task EnsureAsync()
        {
            var connectionKey = _context.Database.GetConnectionString() ?? "default";
            lock (SyncLock)
            {
                if (EnsuredConnections.Contains(connectionKey))
                    return;
            }

            await _context.Database.ExecuteSqlRawAsync(@"
                DO $$
                DECLARE
                    short_column record;
                BEGIN
                    FOR short_column IN
                        SELECT table_schema, table_name, column_name
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND data_type = 'character varying'
                          AND character_maximum_length <= 10
                    LOOP
                        EXECUTE format(
                            'ALTER TABLE %I.%I ALTER COLUMN %I TYPE varchar(80)',
                            short_column.table_schema,
                            short_column.table_name,
                            short_column.column_name);
                    END LOOP;

                    IF to_regclass('public.""Reports""') IS NOT NULL THEN
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""Code"" varchar(160) NOT NULL DEFAULT '';
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsPrintForm"" boolean NOT NULL DEFAULT false;
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""IsDefault"" boolean NOT NULL DEFAULT false;
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""SourceFormat"" varchar(80) NOT NULL DEFAULT 'Native';
                        ALTER TABLE ""Reports"" ADD COLUMN IF NOT EXISTS ""TemplateVersion"" integer NOT NULL DEFAULT 1;
                        ALTER TABLE ""Reports"" ALTER COLUMN ""DataSourceType"" TYPE varchar(80);
                        ALTER TABLE ""Reports"" ALTER COLUMN ""ReportType"" TYPE varchar(120);
                        ALTER TABLE ""Reports"" ALTER COLUMN ""SourceFormat"" TYPE varchar(80);
                        ALTER TABLE ""Reports"" ALTER COLUMN ""PageOrientation"" TYPE varchar(40);
                        ALTER TABLE ""Reports"" ALTER COLUMN ""Icon"" TYPE varchar(40);
                        ALTER TABLE ""Reports"" ALTER COLUMN ""Code"" TYPE varchar(160);
                    END IF;

                    IF to_regclass('public.""LocalizationEntries""') IS NOT NULL THEN
                        ALTER TABLE ""LocalizationEntries"" ADD COLUMN IF NOT EXISTS ""Category"" varchar(120) NOT NULL DEFAULT 'System';
                        ALTER TABLE ""LocalizationEntries"" ADD COLUMN IF NOT EXISTS ""IsActive"" boolean NOT NULL DEFAULT true;
                        ALTER TABLE ""LocalizationEntries"" ADD COLUMN IF NOT EXISTS ""UpdatedAt"" timestamp with time zone NOT NULL DEFAULT NOW();
                        ALTER TABLE ""LocalizationEntries"" ALTER COLUMN ""Culture"" TYPE varchar(40);
                        ALTER TABLE ""LocalizationEntries"" ALTER COLUMN ""Category"" TYPE varchar(120);
                    END IF;

                    IF to_regclass('public.""Materials""') IS NOT NULL THEN
                        ALTER TABLE ""Materials"" ALTER COLUMN ""Unit"" TYPE varchar(50);
                    END IF;

                    IF to_regclass('public.""MetadataModules""') IS NOT NULL THEN
                        ALTER TABLE ""MetadataModules"" ALTER COLUMN ""Icon"" TYPE varchar(40);
                        ALTER TABLE ""MetadataModules"" ALTER COLUMN ""Code"" TYPE varchar(120);
                    END IF;

                    IF to_regclass('public.""MetadataObjects""') IS NOT NULL THEN
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""UsePostings"" boolean NOT NULL DEFAULT false;
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""UseBalances"" boolean NOT NULL DEFAULT false;
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""UseMovements"" boolean NOT NULL DEFAULT false;
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""BalanceTable"" varchar(120);
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""MovementTable"" varchar(120);
                        ALTER TABLE ""MetadataObjects"" ADD COLUMN IF NOT EXISTS ""ReferenceFields"" text;
                        ALTER TABLE ""MetadataObjects"" ALTER COLUMN ""ObjectType"" TYPE varchar(200);
                        ALTER TABLE ""MetadataObjects"" ALTER COLUMN ""TableName"" TYPE varchar(120);
                        ALTER TABLE ""MetadataObjects"" ALTER COLUMN ""Name"" TYPE varchar(160);
                    END IF;

                    IF to_regclass('public.""MetadataModuleItems""') IS NOT NULL THEN
                        ALTER TABLE ""MetadataModuleItems"" ALTER COLUMN ""ObjectType"" TYPE varchar(80);
                    END IF;

                    IF to_regclass('public.catalog_taxes') IS NOT NULL THEN
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS sort_order integer;
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS esf_vat_code varchar(20);
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS esf_sales_tax_code varchar(20);
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS is_default_vat boolean NOT NULL DEFAULT false;
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS is_default_sales_tax boolean NOT NULL DEFAULT false;
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS vat_payable_account varchar(50);
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS vat_recoverable_account varchar(50);
                        ALTER TABLE catalog_taxes ADD COLUMN IF NOT EXISTS sales_tax_account varchar(50);
                        ALTER TABLE catalog_taxes ALTER COLUMN code TYPE varchar(80);
                    END IF;

                    IF to_regclass('public.catalog_payment_kinds') IS NOT NULL THEN
                        ALTER TABLE catalog_payment_kinds ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        ALTER TABLE catalog_payment_kinds ADD COLUMN IF NOT EXISTS rate decimal(18, 2);
                        ALTER TABLE catalog_payment_kinds ADD COLUMN IF NOT EXISTS esf_code varchar(20);
                        ALTER TABLE catalog_payment_kinds ADD COLUMN IF NOT EXISTS is_default boolean NOT NULL DEFAULT false;
                        ALTER TABLE catalog_payment_kinds ALTER COLUMN code TYPE varchar(80);
                    END IF;

                    IF to_regclass('public.catalog_supply_kinds') IS NOT NULL THEN
                        ALTER TABLE catalog_supply_kinds ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        ALTER TABLE catalog_supply_kinds ADD COLUMN IF NOT EXISTS sort_order integer;
                        ALTER TABLE catalog_supply_kinds ADD COLUMN IF NOT EXISTS esf_code varchar(20);
                        ALTER TABLE catalog_supply_kinds ADD COLUMN IF NOT EXISTS is_default boolean NOT NULL DEFAULT false;
                        ALTER TABLE catalog_supply_kinds ALTER COLUMN code TYPE varchar(80);
                    END IF;

                    IF to_regclass('public.catalog_delivery_types') IS NOT NULL THEN
                        ALTER TABLE catalog_delivery_types ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
                        ALTER TABLE catalog_delivery_types ADD COLUMN IF NOT EXISTS sort_order integer;
                        ALTER TABLE catalog_delivery_types ADD COLUMN IF NOT EXISTS esf_code varchar(20);
                        ALTER TABLE catalog_delivery_types ADD COLUMN IF NOT EXISTS is_default boolean NOT NULL DEFAULT false;
                        ALTER TABLE catalog_delivery_types ALTER COLUMN code TYPE varchar(80);
                    END IF;

                    IF to_regclass('public.catalog_cash_desks') IS NOT NULL THEN
                        ALTER TABLE catalog_cash_desks ALTER COLUMN code TYPE varchar(80);
                    END IF;
                END $$;");

            lock (SyncLock)
            {
                EnsuredConnections.Add(connectionKey);
            }
        }
    }
}
