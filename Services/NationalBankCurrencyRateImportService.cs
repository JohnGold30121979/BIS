using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using BIS.ERP.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BIS.ERP.Services
{
    public sealed record CurrencyRateImportResult(DateTime RateDate, int Imported, int Skipped);

    public class NationalBankCurrencyRateImportService
    {
        private static readonly Uri DailyRatesUri = new("https://www.nbkr.kg/XML/daily.xml");
        private static readonly Uri WeeklyRatesUri = new("https://www.nbkr.kg/XML/weekly.xml");

        private readonly AppDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly MetadataService _metadataService;

        public NationalBankCurrencyRateImportService(AppDbContext context, HttpClient? httpClient = null)
        {
            _context = context;
            _httpClient = httpClient ?? new HttpClient();
            _metadataService = new MetadataService(context);
        }

        public async Task<IReadOnlyList<CurrencyRateImportResult>> ImportLatestOfficialRatesAsync(
            bool includeWeeklyRates = true,
            CancellationToken cancellationToken = default)
        {
            var results = new List<CurrencyRateImportResult>
            {
                await ImportFromXmlAsync(DailyRatesUri, cancellationToken)
            };

            if (includeWeeklyRates)
                results.Add(await ImportFromXmlAsync(WeeklyRatesUri, cancellationToken));

            return results;
        }

        private async Task<CurrencyRateImportResult> ImportFromXmlAsync(Uri uri, CancellationToken cancellationToken)
        {
            var xml = await _httpClient.GetStringAsync(uri, cancellationToken);
            var document = XDocument.Parse(xml);
            var root = document.Root ?? throw new InvalidOperationException("НБКР вернул пустой XML.");
            var rateDate = ParseDate(root.Attribute("Date")?.Value) ?? DateTime.Today;

            var currenciesCatalog = await GetCatalogAsync("Справочник валют");
            var ratesCatalog = await GetCatalogAsync("Справочник курсов валют");
            if (currenciesCatalog == null || ratesCatalog == null)
                throw new InvalidOperationException("Не найдены справочники валют или курсов валют.");

            var currencyIds = await LoadCurrencyIdsAsync(currenciesCatalog.Id);
            var imported = 0;
            var skipped = 0;

            foreach (var element in root.Elements().Where(item => item.Name.LocalName.Equals("Currency", StringComparison.OrdinalIgnoreCase)))
            {
                var code = element.Attribute("ISOCode")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    skipped++;
                    continue;
                }

                if (!TryReadDecimal(element.Element("Value")?.Value, out var value) || value <= 0)
                {
                    skipped++;
                    continue;
                }

                var nominal = TryReadDecimal(element.Element("Nominal")?.Value, out var parsedNominal) && parsedNominal > 0
                    ? parsedNominal
                    : 1m;
                var rate = Math.Round(value / nominal, 6, MidpointRounding.AwayFromZero);

                if (!currencyIds.TryGetValue(code, out var currencyId))
                {
                    skipped++;
                    continue;
                }

                await UpsertRateAsync(ratesCatalog.TableName, currencyId, rateDate, rate);
                imported++;
            }

            return new CurrencyRateImportResult(rateDate, imported, skipped);
        }

        private async Task<BIS.ERP.Models.MetadataObject?> GetCatalogAsync(string name)
        {
            return await _context.MetadataObjects.AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.ObjectType == "Catalog" &&
                    item.Name == name);
        }

        private async Task<Dictionary<string, Guid>> LoadCurrencyIdsAsync(Guid catalogId)
        {
            var rows = await _metadataService.GetCatalogDataAsync(catalogId);
            return rows
                .Select(row => new
                {
                    IdText = row.GetValueOrDefault("Id")?.ToString(),
                    Code = row.GetValueOrDefault("Код")?.ToString() ?? row.GetValueOrDefault("code")?.ToString()
                })
                .Where(item => Guid.TryParse(item.IdText, out _) && !string.IsNullOrWhiteSpace(item.Code))
                .ToDictionary(
                    item => item.Code!.Trim(),
                    item => Guid.Parse(item.IdText!),
                    StringComparer.OrdinalIgnoreCase);
        }

        private async Task UpsertRateAsync(string tableName, Guid currencyId, DateTime rateDate, decimal rate)
        {
            var sql = $@"
                INSERT INTO {QuoteIdentifier(tableName)}
                    (""Id"", ""rate_date"", ""currency_id"", ""rate_nb"", ""rate_commercial"",
                     ""is_active"", ""description"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @rateDate, @currencyId, @rate, 0, true, @description, NOW(), NOW())
                ON CONFLICT (""Id"") DO NOTHING;";

            var existingId = await FindExistingRateIdAsync(tableName, currencyId, rateDate);
            if (existingId.HasValue)
            {
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE {QuoteIdentifier(tableName)}
                    SET ""rate_nb"" = @rate,
                        ""description"" = @description,
                        ""is_active"" = true,
                        ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = @id;",
                    new NpgsqlParameter("@id", existingId.Value),
                    new NpgsqlParameter("@rate", rate),
                    new NpgsqlParameter("@description", "Импорт НБКР"));
                return;
            }

            await _context.Database.ExecuteSqlRawAsync(
                sql,
                new NpgsqlParameter("@id", Guid.NewGuid()),
                new NpgsqlParameter("@rateDate", rateDate.Date),
                new NpgsqlParameter("@currencyId", currencyId),
                new NpgsqlParameter("@rate", rate),
                new NpgsqlParameter("@description", "Импорт НБКР"));
        }

        private async Task<Guid?> FindExistingRateIdAsync(string tableName, Guid currencyId, DateTime rateDate)
        {
            await using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = $@"
                SELECT ""Id""
                FROM {QuoteIdentifier(tableName)}
                WHERE currency_id::text = @currencyId
                  AND rate_date::date = @rateDate
                LIMIT 1;";
            command.Parameters.Add(new NpgsqlParameter("@currencyId", currencyId.ToString()));
            command.Parameters.Add(new NpgsqlParameter("@rateDate", rateDate.Date));

            var closeConnection = false;
            try
            {
                if (_context.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
                {
                    await _context.Database.OpenConnectionAsync();
                    closeConnection = true;
                }

                var result = await command.ExecuteScalarAsync();
                return result is Guid id ? id : null;
            }
            finally
            {
                if (closeConnection)
                    await _context.Database.CloseConnectionAsync();
            }
        }

        private static DateTime? ParseDate(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return DateTime.TryParseExact(value.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed)
                ? parsed
                : null;
        }

        private static bool TryReadDecimal(string? text, out decimal value)
        {
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.GetCultureInfo("ru-RU"), out value))
                return true;

            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private static string QuoteIdentifier(string identifier) =>
            "\"" + identifier.Replace("\"", "\"\"") + "\"";
    }
}
