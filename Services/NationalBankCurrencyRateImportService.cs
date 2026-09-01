using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
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

        private static readonly IReadOnlyDictionary<string, string> NationalBankCurrencyIdsByCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = "15",
                ["EUR"] = "20",
                ["RUB"] = "44",
                ["KZT"] = "40",
                ["CNY"] = "24",
                ["AUD"] = "56",
                ["AZN"] = "82",
                ["GBP"] = "17",
                ["AMD"] = "38",
                ["BYN"] = "160",
                ["BRL"] = "101",
                ["HUF"] = "51",
                ["KRW"] = "25",
                ["GEL"] = "102",
                ["DKK"] = "19",
                ["AED"] = "103",
                ["INR"] = "21",
                ["IRR"] = "199",
                ["CAD"] = "23",
                ["KWD"] = "50",
                ["MYR"] = "105",
                ["MDL"] = "43",
                ["MNT"] = "106",
                ["TRY"] = "57",
                ["NZD"] = "53",
                ["TWD"] = "107",
                ["TMT"] = "108",
                ["NOK"] = "28",
                ["PKR"] = "55",
                ["PLN"] = "109",
                ["SGD"] = "86",
                ["TJS"] = "45",
                ["UZS"] = "200",
                ["UAH"] = "47",
                ["CZK"] = "52",
                ["SEK"] = "34",
                ["CHF"] = "35",
                ["JPY"] = "36",
                ["SAR"] = "139",
                ["OMR"] = "180",
                ["HKD"] = "182",
                ["IDR"] = "184",
                ["BHD"] = "202",
                ["VND"] = "204",
                ["THB"] = "206"
            };

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

        public async Task<IReadOnlyList<CurrencyRateImportResult>> ImportOfficialRatesForPeriodAsync(
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken = default)
        {
            var periodStart = startDate.Date;
            var periodEnd = endDate.Date;
            if (periodStart > periodEnd)
                throw new InvalidOperationException("Дата начала периода больше даты окончания.");
            if ((periodEnd - periodStart).TotalDays > 370)
                throw new InvalidOperationException("НБКР ограничивает выборку курсов периодом около одного года. Уменьшите период.");

            var currenciesCatalog = await GetCatalogAsync("Справочник валют");
            var ratesCatalog = await GetCatalogAsync("Справочник курсов валют");
            if (currenciesCatalog == null || ratesCatalog == null)
                throw new InvalidOperationException("Не найдены справочники валют или курсов валют.");

            var currencyIds = await LoadCurrencyIdsAsync(currenciesCatalog.Id);
            var imported = 0;
            var skipped = 0;

            foreach (var currency in currencyIds.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!NationalBankCurrencyIdsByCode.TryGetValue(currency.Key, out var nbkrCurrencyId))
                {
                    skipped++;
                    continue;
                }

                var historyRates = await LoadHistoryRatesAsync(nbkrCurrencyId, periodStart, periodEnd, cancellationToken);
                if (historyRates.Count == 0)
                {
                    skipped++;
                    continue;
                }

                foreach (var rate in historyRates)
                {
                    await UpsertRateAsync(ratesCatalog.TableName, currency.Value, rate.RateDate, rate.Rate);
                    imported++;
                }
            }

            await ActivateLatestRatePerCurrencyAsync(ratesCatalog.TableName);
            return new[] { new CurrencyRateImportResult(periodEnd, imported, skipped) };
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

            await ActivateLatestRatePerCurrencyAsync(ratesCatalog.TableName);

            return new CurrencyRateImportResult(rateDate, imported, skipped);
        }

        private async Task<IReadOnlyList<NationalBankHistoryRate>> LoadHistoryRatesAsync(
            string nbkrCurrencyId,
            DateTime startDate,
            DateTime endDate,
            CancellationToken cancellationToken)
        {
            var uri = BuildHistoryUri(nbkrCurrencyId, startDate, endDate);
            var html = await _httpClient.GetStringAsync(uri, cancellationToken);
            return ParseHistoryRates(html, startDate, endDate);
        }

        private static Uri BuildHistoryUri(string nbkrCurrencyId, DateTime startDate, DateTime endDate)
        {
            var query = string.Join("&", new[]
            {
                "item=1562",
                "lang=RUS",
                "valuta_id=" + Uri.EscapeDataString(nbkrCurrencyId),
                "beg_day=" + startDate.ToString("dd", CultureInfo.InvariantCulture),
                "beg_month=" + startDate.ToString("MM", CultureInfo.InvariantCulture),
                "beg_year=" + startDate.ToString("yyyy", CultureInfo.InvariantCulture),
                "end_day=" + endDate.ToString("dd", CultureInfo.InvariantCulture),
                "end_month=" + endDate.ToString("MM", CultureInfo.InvariantCulture),
                "end_year=" + endDate.ToString("yyyy", CultureInfo.InvariantCulture)
            });
            return new Uri("https://www.nbkr.kg/index1.jsp?" + query);
        }

        private static IReadOnlyList<NationalBankHistoryRate> ParseHistoryRates(
            string html,
            DateTime startDate,
            DateTime endDate)
        {
            var text = WebUtility.HtmlDecode(html ?? string.Empty).Replace('\u00A0', ' ');
            text = Regex.Replace(text, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = Regex.Replace(text, "\\s+", " ");

            var rows = new List<NationalBankHistoryRate>();
            var seenDates = new HashSet<DateTime>();
            foreach (Match match in Regex.Matches(text, @"(?<date>\d{2}\.\d{2}\.\d{4})\s+(?<rate>\d+(?:[,.]\d{1,6}))"))
            {
                var date = ParseDate(match.Groups["date"].Value);
                if (!date.HasValue || date.Value.Date < startDate || date.Value.Date > endDate)
                    continue;
                if (!TryReadDecimal(match.Groups["rate"].Value, out var rate) || rate <= 0)
                    continue;
                if (!seenDates.Add(date.Value.Date))
                    continue;

                rows.Add(new NationalBankHistoryRate(date.Value.Date, rate));
            }

            return rows.OrderBy(row => row.RateDate).ToList();
        }

        /// <summary>
        /// Оставляет активным только последний (по дате курса) курс каждой валюты;
        /// все более старые строки истории переводятся в неактивные.
        /// </summary>
        private async Task ActivateLatestRatePerCurrencyAsync(string tableName)
        {
            var sql = $@"
                WITH latest AS (
                    SELECT DISTINCT ON (currency_id::text) ""Id"", currency_id::text AS currency_key
                    FROM {QuoteIdentifier(tableName)}
                    WHERE currency_id IS NOT NULL
                    ORDER BY currency_id::text, rate_date::date DESC, ""UpdatedAt"" DESC
                )
                UPDATE {QuoteIdentifier(tableName)} AS rates
                SET ""is_active"" = (rates.""Id"" = latest.""Id""),
                    ""UpdatedAt"" = NOW()
                FROM latest
                WHERE rates.currency_id::text = latest.currency_key
                  AND rates.""is_active"" IS DISTINCT FROM (rates.""Id"" = latest.""Id"");";
            await _context.Database.ExecuteSqlRawAsync(sql);
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
            var existingId = await FindExistingRateIdAsync(tableName, currencyId, rateDate);
            if (existingId.HasValue)
            {
                await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE {QuoteIdentifier(tableName)}
                    SET ""rate_nb"" = @rate,
                        ""description"" = @description,
                        ""UpdatedAt"" = NOW()
                    WHERE ""Id"" = @id;",
                    new NpgsqlParameter("@id", existingId.Value),
                    new NpgsqlParameter("@rate", rate),
                    new NpgsqlParameter("@description", "Импорт НБКР"));
                return;
            }

            await _context.Database.ExecuteSqlRawAsync(
                $@"
                INSERT INTO {QuoteIdentifier(tableName)}
                    (""Id"", ""rate_date"", ""currency_id"", ""rate_nb"", ""rate_commercial"",
                     ""is_active"", ""description"", ""CreatedAt"", ""UpdatedAt"")
                VALUES
                    (@id, @rateDate, @currencyId, @rate, 0, false, @description, NOW(), NOW());",
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

        private sealed record NationalBankHistoryRate(DateTime RateDate, decimal Rate);
    }
}
