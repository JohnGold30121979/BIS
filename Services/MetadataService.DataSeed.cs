using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public partial class MetadataService
    {

        private async Task AddCurrencyRatesDataToTable(MetadataObject catalog)
        {
            var currencies = new Dictionary<string, Guid>();

            try
            {
                // Используем обычный SQL запрос через ExecuteReader
                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT \"Id\", \"code\", \"name\" FROM \"catalog_currencies\"";

                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var id = reader.GetGuid(0);        // Id
                    var code = reader.GetString(1);     // code
                    var name = reader.GetString(2);     // name

                    currencies[code] = id;
                    System.Diagnostics.Debug.WriteLine($"Загружена валюта: {code} - {name} - {id}");
                }
                await _context.Database.CloseConnectionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка загрузки валют: {ex.Message}");
                return;
            }

            var rates = new[]
            {
        new { date = new DateTime(2008, 6, 13), code = "USD", name = "доллар", rate_nb = 36.2886m, rate_com = 0.0000m },
        new { date = new DateTime(2008, 6, 13), code = "RUB", name = "рубль", rate_nb = 1.5324m, rate_com = 0.0000m },
        new { date = new DateTime(2008, 6, 13), code = "KZT", name = "тенге", rate_nb = 0.3009m, rate_com = 0.0000m },
        new { date = new DateTime(2008, 6, 13), code = "CNY", name = "юань", rate_nb = 5.5000m, rate_com = 0.0000m },
        new { date = new DateTime(2008, 6, 14), code = "USD", name = "доллар", rate_nb = 36.5000m, rate_com = 37.0000m },
        new { date = new DateTime(2008, 6, 14), code = "RUB", name = "рубль", rate_nb = 1.5400m, rate_com = 1.5600m },
        new { date = new DateTime(2008, 6, 15), code = "USD", name = "доллар", rate_nb = 36.7000m, rate_com = 37.2000m }
    };

            foreach (var rate in rates)
            {
                if (!currencies.ContainsKey(rate.code))
                {
                    System.Diagnostics.Debug.WriteLine($"Валюта с кодом {rate.code} не найдена");
                    continue;
                }

                var currencyId = currencies[rate.code];

                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""rate_date"", ""currency_id"", ""rate_nb"", ""rate_commercial"", ""is_active"", ""description"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{rate.date:yyyy-MM-dd HH:mm:ss}',
                '{currencyId}',
                {rate.rate_nb.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                {rate.rate_com.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                true,
                '',
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }

            System.Diagnostics.Debug.WriteLine($"Добавлено курсов валют: {rates.Length}");
        }

        private async Task AddCurrencyDataToTable(MetadataObject catalog)
        {
            var currencies = new[]
            {
        new { code = "USD", name = "Доллар США", symbol = "$", rate = 85.50m, is_base = true, is_active = true },
        new { code = "RUB", name = "Российский рубль", symbol = "₽", rate = 1.00m, is_base = false, is_active = true },
        new { code = "KZT", name = "Казахстанский тенге", symbol = "₸", rate = 0.18m, is_base = false, is_active = true },
        new { code = "CNY", name = "Китайский юань", symbol = "¥", rate = 11.80m, is_base = false, is_active = true },
        new { code = "EUR", name = "Евро", symbol = "€", rate = 92.30m, is_base = false, is_active = true },
        new { code = "GBP", name = "Фунт стерлингов", symbol = "£", rate = 108.50m, is_base = false, is_active = true },
        new { code = "KGS", name = "Киргизский сом", symbol = "с", rate = 1.00m, is_base = false, is_active = true }
    };

            foreach (var currency in currencies)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""symbol"", ""rate"", ""is_base"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{currency.code}',
                '{currency.name.Replace("'", "''")}',
                '{currency.symbol}',
                {currency.rate.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                {currency.is_base.ToString().ToLower()},
                {currency.is_active.ToString().ToLower()},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }

            System.Diagnostics.Debug.WriteLine($"Добавлено валют: {currencies.Length}");
        }

        private async Task AddMaterialCategoriesDataToTable(MetadataObject catalog)
        {
            var categories = new[]
            {
            new { code = "1", name = "ЗАПАСЫ ОСНОВНОГО ПРОИЗВОДСТВА", description = "Основные производственные запасы", is_active = true },
            new { code = "2", name = "ТОПЛИВО", description = "Топливные материалы", is_active = true },
            new { code = "3", name = "ТАРА", description = "Тара и упаковка", is_active = true },
            new { code = "4", name = "ЗАПЧАСТИ", description = "Запасные части для оборудования", is_active = true },
            new { code = "5", name = "СТРОЙМАТЕРИАЛЫ", description = "Строительные материалы", is_active = true },
            new { code = "6", name = "ПРОЧИЕ МАТЕРИАЛЫ", description = "Прочие материалы", is_active = true }
        };

            foreach (var cat in categories)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{cat.code}',
                '{cat.name.Replace("'", "''")}',
                '{cat.description?.Replace("'", "''") ?? ""}',
                {cat.is_active},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено категорий: {categories.Length}");
        }

        private async Task AddChartOfAccountsDataToTable(MetadataObject catalog)
        {
            var accounts = InitialDataProvider.GetChartOfAccounts();

            foreach (var account in accounts)
            {
                try
                {
                    var sql = $@"
                        INSERT INTO ""{catalog.TableName}"" 
                        (""Id"", ""code"", ""name"", ""account_type"", ""description"", ""level"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
                        VALUES (
                            '{Guid.NewGuid()}',
                            '{account.Code}',
                            '{account.Name.Replace("'", "''")}',
                            '{account.AccountType}',
                            '{account.Description?.Replace("'", "''") ?? ""}',
                            {account.Level},
                            true,
                            NOW(),
                            NOW()
                        )";
                    await _context.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка добавления счета {account.Code}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено счетов: {accounts.Count}");
        }

        private async Task AddBanksDataToTable(MetadataObject catalog)
        {
            var banks = InitialDataProvider.GetBanks();

            foreach (var bank in banks)
            {
                try
                {
                    var sql = $@"
                        INSERT INTO ""{catalog.TableName}"" 
                        (""Id"", ""name"", ""short_name"", ""bic"", ""inn"", ""address"", ""phone"", ""website"", ""email"", ""swift"", ""corr_account"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
                        VALUES (
                            '{Guid.NewGuid()}',
                            '{bank.Name.Replace("'", "''")}',
                            '{bank.ShortName?.Replace("'", "''") ?? ""}',
                            '{bank.BIC}',
                            '{bank.INN}',
                            '{bank.Address?.Replace("'", "''") ?? ""}',
                            '{bank.Phone}',
                            '{bank.Website}',
                            '{bank.Email}',
                            '{bank.SwiftCode}',
                            '{bank.CorrespondentAccount}',
                            true,
                            NOW(),
                            NOW()
                        )";
                    await _context.Database.ExecuteSqlRawAsync(sql);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка добавления банка {bank.Name}: {ex.Message}");
                }
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено банков: {banks.Count}");
        }

        private async Task AddMaterialTypesDataToTable(MetadataObject catalog)
        {
            var materialTypes = new[]
            {
        new { code = "1", name = "ОС", description = "Основные средства", is_active = true },
        new { code = "2", name = "Малоценка", description = "Малоценные предметы", is_active = true },
        new { code = "3", name = "Прочие материалы", description = "Прочие материалы", is_active = true },
        new { code = "8", name = "Спец.одежда", description = "Специальная одежда", is_active = true },
        new { code = "9", name = "Бензин, л", description = "Бензин в литрах", is_active = true },
        new { code = "10", name = "Див. топливо, л", description = "Дизельное топливо в литрах", is_active = true },
        new { code = "11", name = "Авто Масла и про", description = "Автомасла и прочие жидкости", is_active = true },
        new { code = "12", name = "Сера, кг", description = "Сера в килограммах", is_active = true },
        new { code = "13", name = "Тринатрий фосфат, кг", description = "Тринатрий фосфат в кг", is_active = true },
        new { code = "14", name = "Известь хлорная, кг", description = "Известь хлорная в кг", is_active = true },
        new { code = "15", name = "Жир технический, кг", description = "Жир технический в кг", is_active = true },
        new { code = "16", name = "Мешки 50 кг, шт", description = "Мешки 50 кг в штуках", is_active = true },
        new { code = "17", name = "Мешки 25 кг, шт", description = "Мешки 25 кг в штуках", is_active = true },
        new { code = "18", name = "Бирки для мешков 25 кг, л", description = "Бирки для мешков 25 кг", is_active = true }
    };

            foreach (var type in materialTypes)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{type.code}',
                '{type.name.Replace("'", "''")}',
                '{type.description?.Replace("'", "''") ?? ""}',
                {type.is_active.ToString().ToLower()},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено видов материалов: {materialTypes.Length}");
        }
    }
}
