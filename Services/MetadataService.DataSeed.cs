using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
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
                    var id = reader.GetGuid(0);
                    var code = reader.GetString(1);
                    var name = reader.GetString(2);

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
                        (""Id"", ""code"", ""name"", ""account_type"", ""description"", ""level"", ""is_active"",
                         ""closing_module_code"", ""analytic_group"", ""print_mode"", ""balance_mode"",
                         ""link_organizations"", ""link_employees"", ""link_currencies"", ""link_personal_accounts"",
                         ""link_materials"", ""link_sites"", ""tax_code"",
                         ""CreatedAt"", ""UpdatedAt"") 
                        VALUES (
                            '{Guid.NewGuid()}',
                            '{EscapeSql(account.Code)}',
                            '{EscapeSql(account.Name)}',
                            '{EscapeSql(account.AccountType)}',
                            '{EscapeSql(account.Description)}',
                            {account.Level},
                            true,
                            {ToSqlLiteral(account.ClosingSubsystemCode == 0 ? null : account.ClosingSubsystemCode.ToString(CultureInfo.InvariantCulture))},
                            {ToSqlLiteral(account.AnalyticsGroupCode == 0 ? null : account.AnalyticsGroupCode.ToString(CultureInfo.InvariantCulture))},
                            {ToSqlLiteral(account.PrintModeCode == 0 ? null : account.PrintModeCode.ToString(CultureInfo.InvariantCulture))},
                            {ToSqlLiteral(account.BalanceModeCode == 0 ? null : account.BalanceModeCode.ToString(CultureInfo.InvariantCulture))},
                            {account.UseOrganizations.ToString().ToLower()},
                            {account.UseEmployees.ToString().ToLower()},
                            {account.UseCurrencies.ToString().ToLower()},
                            {account.UsePersonalAccounts.ToString().ToLower()},
                            {account.UseMaterials.ToString().ToLower()},
                            {account.UseSites.ToString().ToLower()},
                            {ToSqlLiteral(account.TaxCode == 0 ? null : account.TaxCode.ToString(CultureInfo.InvariantCulture))},
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

        public async Task EnsureCashOrderPostingAccountsAsync()
        {
            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(item => item.ObjectType == "Catalog" && item.Name == "План счетов");
            if (catalog == null)
                return;

            var accounts = InitialDataProvider.GetChartOfAccounts()
                .Where(item => item.Code is "4010" or "6010" or "6810" or "6850");

            foreach (var account in accounts)
            {
                var sql = $@"
                    INSERT INTO ""{catalog.TableName}""
                    (""Id"", ""code"", ""name"", ""account_type"", ""description"", ""level"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                    SELECT
                        '{Guid.NewGuid()}',
                        '{EscapeSql(account.Code)}',
                        '{EscapeSql(account.Name)}',
                        '{EscapeSql(account.AccountType)}',
                        '{EscapeSql(account.Description)}',
                        {account.Level},
                        true,
                        NOW(),
                        NOW()
                    WHERE NOT EXISTS (
                        SELECT 1 FROM ""{catalog.TableName}"" WHERE ""code"" = '{EscapeSql(account.Code)}'
                    )";

                await _context.Database.ExecuteSqlRawAsync(sql);
            }
        }

        private async Task EnsureAccountAnalyticsLinksDataAsync(MetadataObject catalog)
        {
            foreach (var link in AccountAnalyticsDefaultLinks.Items)
            {
                var sql = $@"
                    INSERT INTO ""{catalog.TableName}""
                    (""Id"", ""code"", ""name"", ""account_flag_field"", ""reference_catalog"", ""document_fields"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                    SELECT
                        '{Guid.NewGuid()}',
                        '{EscapeSql(link.Code)}',
                        '{EscapeSql(link.Name)}',
                        '{EscapeSql(link.AccountFlagField)}',
                        '{EscapeSql(link.ReferenceCatalog)}',
                        '{EscapeSql(link.DocumentFields)}',
                        '{EscapeSql(link.Description)}',
                        {link.IsActive.ToString().ToLower()},
                        NOW(),
                        NOW()
                    WHERE NOT EXISTS (
                        SELECT 1 FROM ""{catalog.TableName}"" WHERE ""code"" = '{EscapeSql(link.Code)}'
                    )";

                await _context.Database.ExecuteSqlRawAsync(sql);
            }
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
                (""Id"", ""code"", ""name"", ""bic"", ""branch"", ""address"", ""phone"", 
                 ""swift"", ""chips"", ""address_eng"", ""is_active"", ""description"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES (
                    '{Guid.NewGuid()}',
                    '{bank.Code}',
                    '{bank.Name.Replace("'", "''")}',
                    '{bank.BIC}',
                    '{bank.Branch.Replace("'", "''")}',
                    '{bank.Address.Replace("'", "''")}',
                    '{bank.Phone}',
                    '{bank.Swift}',
                    '{bank.Chips}',
                    '{bank.AddressEng.Replace("'", "''")}',
                     {bank.IsActive.ToString().ToLower()},
                    '{bank.Description?.Replace("'", "''") ?? ""}',
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

        private async Task AddCountriesDataToTable(MetadataObject catalog)
        {
            var countries = new[]
           {
            new { code = "AF", name = "Афганистан", is_active = true },
            new { code = "AL", name = "Албания", is_active = true },
            new { code = "DZ", name = "Алжир", is_active = true },
            new { code = "AD", name = "Андорра", is_active = true },
            new { code = "AO", name = "Ангола", is_active = true },
            new { code = "AG", name = "Антигуа и Барбуда", is_active = true },
            new { code = "AR", name = "Аргентина", is_active = true },
            new { code = "AM", name = "Армения", is_active = true },
            new { code = "AU", name = "Австралия", is_active = true },
            new { code = "AT", name = "Австрия", is_active = true },
            new { code = "AZ", name = "Азербайджан", is_active = true },
            new { code = "BS", name = "Багамы", is_active = true },
            new { code = "BH", name = "Бахрейн", is_active = true },
            new { code = "BD", name = "Бангладеш", is_active = true },
            new { code = "BB", name = "Барбадос", is_active = true },
            new { code = "BY", name = "Беларусь", is_active = true },
            new { code = "BE", name = "Бельгия", is_active = true },
            new { code = "BZ", name = "Белиз", is_active = true },
            new { code = "BJ", name = "Бенин", is_active = true },
            new { code = "BT", name = "Бутан", is_active = true },
            new { code = "BO", name = "Боливия", is_active = true },
            new { code = "BA", name = "Босния и Герцеговина", is_active = true },
            new { code = "BW", name = "Ботсвана", is_active = true },
            new { code = "BR", name = "Бразилия", is_active = true },
            new { code = "BN", name = "Бруней", is_active = true },
            new { code = "BG", name = "Болгария", is_active = true },
            new { code = "BF", name = "Буркина-Фасо", is_active = true },
            new { code = "BI", name = "Бурунди", is_active = true },
            new { code = "KH", name = "Камбоджа", is_active = true },
            new { code = "CM", name = "Камерун", is_active = true },
            new { code = "CA", name = "Канада", is_active = true },
            new { code = "CV", name = "Кабо-Верде", is_active = true },
            new { code = "CF", name = "Центральноафриканская Республика", is_active = true },
            new { code = "TD", name = "Чад", is_active = true },
            new { code = "CL", name = "Чили", is_active = true },
            new { code = "CN", name = "Китай", is_active = true },
            new { code = "CO", name = "Колумбия", is_active = true },
            new { code = "KM", name = "Коморы", is_active = true },
            new { code = "CG", name = "Конго", is_active = true },
            new { code = "CD", name = "Демократическая Республика Конго", is_active = true },
            new { code = "CR", name = "Коста-Рика", is_active = true },
            new { code = "CI", name = "Кот-д'Ивуар", is_active = true },
            new { code = "HR", name = "Хорватия", is_active = true },
            new { code = "CU", name = "Куба", is_active = true },
            new { code = "CY", name = "Кипр", is_active = true },
            new { code = "CZ", name = "Чехия", is_active = true },
            new { code = "DK", name = "Дания", is_active = true },
            new { code = "DJ", name = "Джибути", is_active = true },
            new { code = "DM", name = "Доминика", is_active = true },
            new { code = "DO", name = "Доминиканская Республика", is_active = true },
            new { code = "EC", name = "Эквадор", is_active = true },
            new { code = "EG", name = "Египет", is_active = true },
            new { code = "SV", name = "Сальвадор", is_active = true },
            new { code = "GQ", name = "Экваториальная Гвинея", is_active = true },
            new { code = "ER", name = "Эритрея", is_active = true },
            new { code = "EE", name = "Эстония", is_active = true },
            new { code = "SZ", name = "Эсватини", is_active = true },
            new { code = "ET", name = "Эфиопия", is_active = true },
            new { code = "FJ", name = "Фиджи", is_active = true },
            new { code = "FI", name = "Финляндия", is_active = true },
            new { code = "FR", name = "Франция", is_active = true },
            new { code = "GA", name = "Габон", is_active = true },
            new { code = "GM", name = "Гамбия", is_active = true },
            new { code = "GE", name = "Грузия", is_active = true },
            new { code = "DE", name = "Германия", is_active = true },
            new { code = "GH", name = "Гана", is_active = true },
            new { code = "GR", name = "Греция", is_active = true },
            new { code = "GD", name = "Гренада", is_active = true },
            new { code = "GT", name = "Гватемала", is_active = true },
            new { code = "GN", name = "Гвинея", is_active = true },
            new { code = "GW", name = "Гвинея-Бисау", is_active = true },
            new { code = "GY", name = "Гайана", is_active = true },
            new { code = "HT", name = "Гаити", is_active = true },
            new { code = "HN", name = "Гондурас", is_active = true },
            new { code = "HU", name = "Венгрия", is_active = true },
            new { code = "IS", name = "Исландия", is_active = true },
            new { code = "IN", name = "Индия", is_active = true },
            new { code = "ID", name = "Индонезия", is_active = true },
            new { code = "IR", name = "Иран", is_active = true },
            new { code = "IQ", name = "Ирак", is_active = true },
            new { code = "IE", name = "Ирландия", is_active = true },
            new { code = "IL", name = "Израиль", is_active = true },
            new { code = "IT", name = "Италия", is_active = true },
            new { code = "JM", name = "Ямайка", is_active = true },
            new { code = "JP", name = "Япония", is_active = true },
            new { code = "JO", name = "Иордания", is_active = true },
            new { code = "KZ", name = "Казахстан", is_active = true },
            new { code = "KE", name = "Кения", is_active = true },
            new { code = "KI", name = "Кирибати", is_active = true },
            new { code = "KP", name = "Северная Корея", is_active = true },
            new { code = "KR", name = "Южная Корея", is_active = true },
            new { code = "KW", name = "Кувейт", is_active = true },
            new { code = "KG", name = "Кыргызстан", is_active = true },
            new { code = "LA", name = "Лаос", is_active = true },
            new { code = "LV", name = "Латвия", is_active = true },
            new { code = "LB", name = "Ливан", is_active = true },
            new { code = "LS", name = "Лесото", is_active = true },
            new { code = "LR", name = "Либерия", is_active = true },
            new { code = "LY", name = "Ливия", is_active = true },
            new { code = "LI", name = "Лихтенштейн", is_active = true },
            new { code = "LT", name = "Литва", is_active = true },
            new { code = "LU", name = "Люксембург", is_active = true },
            new { code = "MG", name = "Мадагаскар", is_active = true },
            new { code = "MW", name = "Малави", is_active = true },
            new { code = "MY", name = "Малайзия", is_active = true },
            new { code = "MV", name = "Мальдивы", is_active = true },
            new { code = "ML", name = "Мали", is_active = true },
            new { code = "MT", name = "Мальта", is_active = true },
            new { code = "MH", name = "Маршалловы Острова", is_active = true },
            new { code = "MR", name = "Мавритания", is_active = true },
            new { code = "MU", name = "Маврикий", is_active = true },
            new { code = "MX", name = "Мексика", is_active = true },
            new { code = "FM", name = "Микронезия", is_active = true },
            new { code = "MD", name = "Молдова", is_active = true },
            new { code = "MC", name = "Монако", is_active = true },
            new { code = "MN", name = "Монголия", is_active = true },
            new { code = "ME", name = "Черногория", is_active = true },
            new { code = "MA", name = "Марокко", is_active = true },
            new { code = "MZ", name = "Мозамбик", is_active = true },
            new { code = "MM", name = "Мьянма", is_active = true },
            new { code = "NA", name = "Намибия", is_active = true },
            new { code = "NR", name = "Науру", is_active = true },
            new { code = "NP", name = "Непал", is_active = true },
            new { code = "NL", name = "Нидерланды", is_active = true },
            new { code = "NZ", name = "Новая Зеландия", is_active = true },
            new { code = "NI", name = "Никарагуа", is_active = true },
            new { code = "NE", name = "Нигер", is_active = true },
            new { code = "NG", name = "Нигерия", is_active = true },
            new { code = "NO", name = "Норвегия", is_active = true },
            new { code = "OM", name = "Оман", is_active = true },
            new { code = "PK", name = "Пакистан", is_active = true },
            new { code = "PW", name = "Палау", is_active = true },
            new { code = "PA", name = "Панама", is_active = true },
            new { code = "PG", name = "Папуа-Новая Гвинея", is_active = true },
            new { code = "PY", name = "Парагвай", is_active = true },
            new { code = "PE", name = "Перу", is_active = true },
            new { code = "PH", name = "Филиппины", is_active = true },
            new { code = "PL", name = "Польша", is_active = true },
            new { code = "PT", name = "Португалия", is_active = true },
            new { code = "QA", name = "Катар", is_active = true },
            new { code = "RO", name = "Румыния", is_active = true },
            new { code = "RU", name = "Россия", is_active = true },
            new { code = "RW", name = "Руанда", is_active = true },
            new { code = "KN", name = "Сент-Китс и Невис", is_active = true },
            new { code = "LC", name = "Сент-Люсия", is_active = true },
            new { code = "VC", name = "Сент-Винсент и Гренадины", is_active = true },
            new { code = "WS", name = "Самоа", is_active = true },
            new { code = "SM", name = "Сан-Марино", is_active = true },
            new { code = "ST", name = "Сан-Томе и Принсипи", is_active = true },
            new { code = "SA", name = "Саудовская Аравия", is_active = true },
            new { code = "SN", name = "Сенегал", is_active = true },
            new { code = "RS", name = "Сербия", is_active = true },
            new { code = "SC", name = "Сейшелы", is_active = true },
            new { code = "SL", name = "Сьерра-Леоне", is_active = true },
            new { code = "SG", name = "Сингапур", is_active = true },
            new { code = "SK", name = "Словакия", is_active = true },
            new { code = "SI", name = "Словения", is_active = true },
            new { code = "SB", name = "Соломоновы Острова", is_active = true },
            new { code = "SO", name = "Сомали", is_active = true },
            new { code = "ZA", name = "Южная Африка", is_active = true },
            new { code = "SS", name = "Южный Судан", is_active = true },
            new { code = "ES", name = "Испания", is_active = true },
            new { code = "LK", name = "Шри-Ланка", is_active = true },
            new { code = "SD", name = "Судан", is_active = true },
            new { code = "SR", name = "Суринам", is_active = true },
            new { code = "SE", name = "Швеция", is_active = true },
            new { code = "CH", name = "Швейцария", is_active = true },
            new { code = "SY", name = "Сирия", is_active = true },
            new { code = "TW", name = "Тайвань", is_active = true },
            new { code = "TJ", name = "Таджикистан", is_active = true },
            new { code = "TZ", name = "Танзания", is_active = true },
            new { code = "TH", name = "Таиланд", is_active = true },
            new { code = "TL", name = "Тимор-Лесте", is_active = true },
            new { code = "TG", name = "Того", is_active = true },
            new { code = "TO", name = "Тонга", is_active = true },
            new { code = "TT", name = "Тринидад и Тобаго", is_active = true },
            new { code = "TN", name = "Тунис", is_active = true },
            new { code = "TR", name = "Турция", is_active = true },
            new { code = "TM", name = "Туркменистан", is_active = true },
            new { code = "TV", name = "Тувалу", is_active = true },
            new { code = "UG", name = "Уганда", is_active = true },
            new { code = "UA", name = "Украина", is_active = true },
            new { code = "AE", name = "Объединенные Арабские Эмираты", is_active = true },
            new { code = "GB", name = "Великобритания", is_active = true },
            new { code = "US", name = "США", is_active = true },
            new { code = "UY", name = "Уругвай", is_active = true },
            new { code = "UZ", name = "Узбекистан", is_active = true },
            new { code = "VU", name = "Вануату", is_active = true },
            new { code = "VA", name = "Ватикан", is_active = true },
            new { code = "VE", name = "Венесуэла", is_active = true },
            new { code = "VN", name = "Вьетнам", is_active = true },
            new { code = "YE", name = "Йемен", is_active = true },
            new { code = "ZM", name = "Замбия", is_active = true },
            new { code = "ZW", name = "Зимбабве", is_active = true }
            };

            foreach (var country in countries)
            {
                var sql = $@"
                INSERT INTO ""{catalog.TableName}"" 
                (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
                VALUES (
                    '{Guid.NewGuid()}',
                    '{country.code}',
                    '{country.name.Replace("'", "''")}',
                    '',
                    {country.is_active.ToString().ToLower()},
                    NOW(),
                    NOW()
                )";
                        await _context.Database.ExecuteSqlRawAsync(sql);
            }

            System.Diagnostics.Debug.WriteLine($"Добавлено государств: {countries.Length}");
        }

        private async Task AddInitialCashDeskData(MetadataObject catalog)
        {
            await Task.CompletedTask;
            System.Diagnostics.Debug.WriteLine("Автоматическое создание кассы пропущено: счет кассы задается пользователем в справочнике касс.");
        }

        private async Task AddPrimaryOrganizationDataToTable(MetadataObject catalog)
        {
            var insertSql = $@"
                INSERT INTO ""{catalog.TableName}""
                (""Id"", ""code"", ""name"", ""is_primary"", ""full_name"", ""legal_form"", ""inn"", ""okpo"",
                 ""registration_number"", ""legal_address"", ""actual_address"", ""phone"", ""email"",
                 ""bank_name"", ""bank_account"", ""bic"", ""director"", ""chief_accountant"",
                 ""group_code"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                VALUES (
                    '{Guid.NewGuid()}',
                    '0001',
                    'Основное предприятие',
                    true,
                    'Основное предприятие',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    '',
                    'OWN',
                    'Первичная организация. Заполните реквизиты предприятия для печатных форм.',
                    true,
                    NOW(),
                    NOW()
                )";

            await _context.Database.ExecuteSqlRawAsync(insertSql);
        }

        private async Task EnsurePrimaryOrganizationDataAsync(MetadataObject catalog)
        {
            try
            {
                var checkSql = $@"SELECT COUNT(*) FROM ""{catalog.TableName}""";
                using var command = _context.Database.GetDbConnection().CreateCommand();
                command.CommandText = checkSql;

                await _context.Database.OpenConnectionAsync();
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                await _context.Database.CloseConnectionAsync();

                if (count == 0)
                {
                    await AddPrimaryOrganizationDataToTable(catalog);
                    return;
                }

                var primarySql = $@"
                    UPDATE ""{catalog.TableName}""
                    SET ""is_primary"" = CASE
                        WHEN ""Id"" = (
                            SELECT ""Id""
                            FROM ""{catalog.TableName}""
                            ORDER BY COALESCE(""is_primary"", false) DESC, ""code"", ""CreatedAt""
                            LIMIT 1
                        ) THEN true
                        ELSE false
                    END,
                    ""UpdatedAt"" = NOW()";

                await _context.Database.ExecuteSqlRawAsync(primarySql);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка проверки первичной организации: {ex.Message}");
            }
        }

        #region Начальные данные для новых справочников
        
        // Начальные данные для справочника "Налоги"
        private async Task AddTaxDataToTable(MetadataObject catalog)
        {
            var tableName = catalog.TableName;
            var taxes = new[]
            {
        new { code = "НДС12", name = "НДС 12%", rate = 12m, esf_vat_code = "10", esf_sales_tax_code = "", vat_payable_account = "34300000", vat_recoverable_account = "15400000", sales_tax_account = "", is_active = true, sort_order = 1, is_default_vat = true, is_default_sales_tax = false },
        new { code = "НДС0", name = "НДС 0%", rate = 0m, esf_vat_code = "10", esf_sales_tax_code = "", vat_payable_account = "34300000", vat_recoverable_account = "15400000", sales_tax_account = "", is_active = true, sort_order = 2, is_default_vat = false, is_default_sales_tax = false },
        new { code = "WITHOUT_TAX", name = "Без НДС / освобождено", rate = 0m, esf_vat_code = "90", esf_sales_tax_code = "50", vat_payable_account = "34300000", vat_recoverable_account = "15400000", sales_tax_account = "34900000", is_active = true, sort_order = 3, is_default_vat = false, is_default_sales_tax = true },
        new { code = "SALES_TAX", name = "Налог с продаж (базовый режим)", rate = 1.5m, esf_vat_code = "", esf_sales_tax_code = "50", vat_payable_account = "", vat_recoverable_account = "", sales_tax_account = "34900000", is_active = true, sort_order = 4, is_default_vat = false, is_default_sales_tax = false },
        new { code = "SALES_SERVICE", name = "Налог с продаж: услуги (неторг. деятельность)", rate = 2.5m, esf_vat_code = "", esf_sales_tax_code = "70", vat_payable_account = "", vat_recoverable_account = "", sales_tax_account = "34900000", is_active = true, sort_order = 5, is_default_vat = false, is_default_sales_tax = false },
        new { code = "SALES_TRADE", name = "Налог с продаж: торговая деятельность", rate = 1.5m, esf_vat_code = "", esf_sales_tax_code = "50", vat_payable_account = "", vat_recoverable_account = "", sales_tax_account = "34900000", is_active = true, sort_order = 6, is_default_vat = false, is_default_sales_tax = false },
        new { code = "SALES_EXEMPT", name = "Налог с продаж: необлагаемая деятельность", rate = 0m, esf_vat_code = "", esf_sales_tax_code = "50", vat_payable_account = "", vat_recoverable_account = "", sales_tax_account = "34900000", is_active = true, sort_order = 7, is_default_vat = false, is_default_sales_tax = false },
        new { code = "SALES_RETAIL_2009", name = "Налог с продаж: розничная продажа до 2009", rate = 4m, esf_vat_code = "", esf_sales_tax_code = "50", vat_payable_account = "", vat_recoverable_account = "", sales_tax_account = "34900000", is_active = true, sort_order = 8, is_default_vat = false, is_default_sales_tax = false }
        };

            foreach (var tax in taxes)
            {
                try
                {
                    await UpsertCatalogSeedRowAsync(tableName, tax.code, new Dictionary<string, object?>
                    {
                        ["code"] = tax.code,
                        ["name"] = tax.name,
                        ["rate"] = tax.rate,
                        ["esf_vat_code"] = tax.esf_vat_code,
                        ["esf_sales_tax_code"] = tax.esf_sales_tax_code,
                        ["vat_payable_account"] = tax.vat_payable_account,
                        ["vat_recoverable_account"] = tax.vat_recoverable_account,
                        ["sales_tax_account"] = tax.sales_tax_account,
                        ["is_active"] = tax.is_active,
                        ["sort_order"] = tax.sort_order,
                        ["is_default_vat"] = tax.is_default_vat,
                        ["is_default_sales_tax"] = tax.is_default_sales_tax,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["UpdatedAt"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка заполнения {tax.code}: {ex.Message}");
                }
            }
        }
        
        // Начальные данные для справочника "Виды поставки"
        private async Task AddSupplyKindDataToTable(MetadataObject catalog)
        {
            var tableName = catalog.TableName;
            var items = new[]
            {
            new { code = "GOODS", name = "Поставка товаров", description = "Используется для стандартной товарной поставки ЭСФ.", esf_code = "100", is_active = true, sort_order = 1, is_default = true },
            new { code = "SERVICE", name = "Работы / услуги", description = "Наблюдалось в FoxPro-выгрузках как код 101.", esf_code = "101", is_active = true, sort_order = 2, is_default = false },
            new { code = "OTHER", name = "Прочая поставка", description = "Наблюдалось в FoxPro-выгрузках как код 299.", esf_code = "299", is_active = true, sort_order = 3, is_default = false }
            };

            foreach (var item in items)
            {
                try
                {
                    await UpsertCatalogSeedRowAsync(tableName, item.code, new Dictionary<string, object?>
                    {
                        ["code"] = item.code,
                        ["name"] = item.name,
                        ["description"] = item.description,
                        ["esf_code"] = item.esf_code,
                        ["is_active"] = item.is_active,
                        ["sort_order"] = item.sort_order,
                        ["is_default"] = item.is_default,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["UpdatedAt"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка заполнения {item.code}: {ex.Message}");
                }
            }
        }
        
        // Начальные данные для справочника "Виды оплаты"       
        private async Task AddPaymentKindDataToTable(MetadataObject catalog)
        {
            var tableName = catalog.TableName;
            var items = new[]
            {
            new { code = "CASH", name = "Наличные", rate = 0m, esf_code = "10", is_active = true, is_default = false },
            new { code = "CARD", name = "Банковская карта", rate = 0m, esf_code = "11", is_active = true, is_default = false },
            new { code = "TRANSFER", name = "Безналичный перевод", rate = 0m, esf_code = "20", is_active = true, is_default = true },
            new { code = "CHEQUE", name = "Чек", rate = 0m, esf_code = "30", is_active = true, is_default = false }
            };

            foreach (var item in items)
            {
                try
                {
                    await UpsertCatalogSeedRowAsync(tableName, item.code, new Dictionary<string, object?>
                    {
                        ["code"] = item.code,
                        ["name"] = item.name,
                        ["rate"] = item.rate,
                        ["esf_code"] = item.esf_code,
                        ["is_active"] = item.is_active,
                        ["is_default"] = item.is_default,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["UpdatedAt"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка заполнения {item.code}: {ex.Message}");
                }
            }
        }
      
        // Начальные данные для справочника "Типы поставки"
        private async Task AddDeliveryTypeDataToTable(MetadataObject catalog)
        {
            var tableName = catalog.TableName;
            var items = new[]
            {
            new { code = "TAXABLE", name = "Облагаемая поставка", description = "FoxPro/XML: vatDeliveryTypeCode=100.", esf_code = "100", is_active = true, sort_order = 1, is_default = true },
            new { code = "EXEMPT", name = "Необлагаемая / без НДС", description = "FoxPro/XML: vatDeliveryTypeCode=101.", esf_code = "101", is_active = true, sort_order = 2, is_default = false },
            new { code = "IMPORT", name = "Импорт", description = "Резерв под vatDeliveryTypeCode=200.", esf_code = "200", is_active = true, sort_order = 3, is_default = false },
            new { code = "EXPORT", name = "Экспорт", description = "Резерв под vatDeliveryTypeCode=300.", esf_code = "300", is_active = true, sort_order = 4, is_default = false }
            };

            foreach (var item in items)
            {
                try
                {
                    await UpsertCatalogSeedRowAsync(tableName, item.code, new Dictionary<string, object?>
                    {
                        ["code"] = item.code,
                        ["name"] = item.name,
                        ["description"] = item.description,
                        ["esf_code"] = item.esf_code,
                        ["is_active"] = item.is_active,
                        ["sort_order"] = item.sort_order,
                        ["is_default"] = item.is_default,
                        ["CreatedAt"] = DateTime.UtcNow,
                        ["UpdatedAt"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка заполнения {item.code}: {ex.Message}");
                }
            }
        }

        #endregion

        // Начальные данные для справочника "Должности"
        private async Task AddPositionDataToTable(MetadataObject catalog)
        {
            var positions = new[]
            {
                new { code = "DIR", name = "Директор", description = "Генеральный директор", is_active = true },
                new { code = "ACCT", name = "Бухгалтер", description = "Главный бухгалтер", is_active = true },
                new { code = "ECON", name = "Экономист", description = "Экономист", is_active = true }
            };

            foreach (var position in positions)
            {
                var sql = $@"
            INSERT INTO ""{catalog.TableName}"" 
            (""Id"", ""code"", ""name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"") 
            VALUES (
                '{Guid.NewGuid()}',
                '{position.code}',
                '{position.name.Replace("'", "''")}',
                '{position.description?.Replace("'", "''") ?? ""}',
                {position.is_active.ToString().ToLower()},
                NOW(),
                NOW()
            )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено должностей: {positions.Length}");
        }

        private static string EscapeSql(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private async Task UpsertCatalogSeedRowAsync(
            string tableName,
            string code,
            IReadOnlyDictionary<string, object?> values)
        {
            var codeLiteral = ToSqlLiteral(code);
            var updateAssignments = values
                .Where(item => !item.Key.Equals("Id", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("code", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("CreatedAt", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default_vat", StringComparison.OrdinalIgnoreCase) &&
                               !item.Key.Equals("is_default_sales_tax", StringComparison.OrdinalIgnoreCase))
                .Select(item => $@"""{item.Key}"" = {ToSqlLiteral(item.Value)}")
                .ToList();

            if (updateAssignments.Count > 0)
            {
                var updated = await _context.Database.ExecuteSqlRawAsync($@"
                    UPDATE ""{tableName}""
                    SET {string.Join(", ", updateAssignments)}
                    WHERE ""code"" = {codeLiteral};");

                if (updated > 0)
                    return;
            }

            var columnSql = string.Join(", ", values.Keys.Select(item => $@"""{item}"""));
            var valueSql = string.Join(", ", values.Values.Select(ToSqlLiteral));
            await _context.Database.ExecuteSqlRawAsync($@"
                INSERT INTO ""{tableName}"" ({columnSql})
                SELECT {valueSql}
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM ""{tableName}""
                    WHERE ""code"" = {codeLiteral}
                );");
        }

        private static string ToSqlLiteral(object? value)
        {
            return value switch
            {
                null => "NULL",
                DBNull _ => "NULL",
                bool boolean => boolean ? "true" : "false",
                decimal number => number.ToString(CultureInfo.InvariantCulture),
                double number => number.ToString(CultureInfo.InvariantCulture),
                float number => number.ToString(CultureInfo.InvariantCulture),
                int number => number.ToString(CultureInfo.InvariantCulture),
                long number => number.ToString(CultureInfo.InvariantCulture),
                Guid guid => $"'{guid}'",
                DateTime dateTime => $"'{dateTime:yyyy-MM-dd HH:mm:ss}'",
                _ => $"'{EscapeSql(value.ToString() ?? string.Empty)}'"
            };
        }

        private async Task AddSiteDataToTable(MetadataObject catalog)
        {
            var sites = new[]
            {
                new { code = "01", name = "Цех основной", desc = "Основной производственный цех", active = true },
                new { code = "02", name = "Склад", desc = "Склад готовой продукции", active = true },
                new { code = "03", name = "Администрация", desc = "Административный участок", active = true },
                new { code = "04", name = "Торговый зал", desc = "Торговый зал / магазин", active = true },
                new { code = "05", name = "Транспортный цех", desc = "Транспортный участок", active = true }
            };

            foreach (var site in sites)
            {
                var sql = $@"
                    INSERT INTO ""{catalog.TableName}""
                    (""Id"", ""site_code"", ""site_name"", ""description"", ""is_active"", ""CreatedAt"", ""UpdatedAt"")
                    VALUES (
                        '{Guid.NewGuid()}',
                        '{site.code}',
                        '{site.name.Replace("'", "''")}',
                        '{site.desc.Replace("'", "''")}',
                        {site.active.ToString().ToLower()},
                        NOW(),
                        NOW()
                    )";
                await _context.Database.ExecuteSqlRawAsync(sql);
            }
            System.Diagnostics.Debug.WriteLine($"Добавлено участков: {sites.Length}");
        }
    }
}
