using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public static class AccountAnalyticsCatalogNames
    {
        public const string CatalogName = "Связи счетов со справочниками";
    }

    public sealed class AccountAnalyticsLinkDefinition
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AccountFlagField { get; set; } = string.Empty;
        public string ReferenceCatalog { get; set; } = string.Empty;
        public string DocumentFields { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;

        public IReadOnlyList<string> DocumentFieldNames => SplitNames(DocumentFields);

        public bool Matches(MetadataField field)
        {
            return Matches(field.Name, field.ReferenceCatalog);
        }

        public bool Matches(string fieldName, string? referenceCatalog = null)
        {
            if (!IsActive)
                return false;

            if (!string.IsNullOrWhiteSpace(referenceCatalog) &&
                referenceCatalog.Equals(ReferenceCatalog, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var normalizedFieldName = NormalizeName(fieldName);
            return DocumentFieldNames.Any(name =>
                NormalizeName(name).Equals(normalizedFieldName, StringComparison.OrdinalIgnoreCase));
        }

        private static IReadOnlyList<string> SplitNames(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToList();
        }

        private static string NormalizeName(string value)
        {
            return (value ?? string.Empty)
                .Replace(".", string.Empty)
                .Replace("-", " ")
                .Trim()
                .ToLowerInvariant();
        }
    }

    public static class AccountAnalyticsDefaultLinks
    {
        public static IReadOnlyList<AccountAnalyticsLinkDefinition> Items { get; } =
            new List<AccountAnalyticsLinkDefinition>
            {
                new AccountAnalyticsLinkDefinition
                {
                    Code = "organizations",
                    Name = "Организации",
                    AccountFlagField = "Связь с организациями",
                    ReferenceCatalog = "Организации",
                    DocumentFields = "Организация",
                    Description = "Показывает поле организации, если счет ведется в разрезе организаций"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "employees",
                    Name = "Списочный состав / табельный номер",
                    AccountFlagField = "Связь со списочным составом",
                    ReferenceCatalog = "Сотрудники (Списочный состав)",
                    DocumentFields = "Сотрудник;Табельный номер;Таб. номер;Таб №;Таб. №",
                    Description = "Показывает справочник сотрудников для счетов с аналитикой по табельным номерам"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "currencies",
                    Name = "Валюты",
                    AccountFlagField = "Связь с валютами",
                    ReferenceCatalog = "Справочник валют",
                    DocumentFields = "Валюта",
                    Description = "Показывает валюту, если счет ведется в валютном разрезе"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "materials",
                    Name = "Материалы",
                    AccountFlagField = "Связь с материалами",
                    ReferenceCatalog = "Справочник материалов",
                    DocumentFields = "Материал;Материалы;Номенклатура",
                    Description = "Показывает справочник материалов для материальных счетов"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "sites",
                    Name = "Участки",
                    AccountFlagField = "Связь с участками",
                    ReferenceCatalog = "Участки",
                    DocumentFields = "Участок;Участки",
                    Description = "Показывает участок, если счет ведется в разрезе участков"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "personal_accounts",
                    Name = "Лицевые счета",
                    AccountFlagField = "Связь с лицевыми счетами",
                    ReferenceCatalog = "Лицевые счета",
                    DocumentFields = "Лицевой счет;Лицевые счета",
                    Description = "Резерв под аналитику по лицевым счетам"
                },
                new AccountAnalyticsLinkDefinition
                {
                    Code = "construction_objects",
                    Name = "Объекты строительства",
                    AccountFlagField = "Связь с объектами строительства",
                    ReferenceCatalog = "Объекты строительства",
                    DocumentFields = "Объект строительства;Объект;Объекты строительства",
                    Description = "Резерв под аналитику по объектам строительства"
                }
            };
    }

    public sealed class AccountReferenceItem
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string ClosingModule { get; set; } = string.Empty;
    }

    public sealed class AccountAnalyticsSettings
    {
        private readonly HashSet<string> _enabledLinkCodes = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyCollection<string> EnabledLinkCodes => _enabledLinkCodes;

        public void Enable(string code)
        {
            if (!string.IsNullOrWhiteSpace(code))
                _enabledLinkCodes.Add(code);
        }

        public bool Allows(AccountAnalyticsLinkDefinition definition)
        {
            return definition.IsActive && _enabledLinkCodes.Contains(definition.Code);
        }
    }

    public sealed class AccountAnalyticsRegistry
    {
        private readonly Dictionary<Guid, AccountAnalyticsSettings> _settingsById = new();
        private readonly Dictionary<string, AccountAnalyticsSettings> _settingsByCode =
            new(StringComparer.OrdinalIgnoreCase);

        public List<AccountReferenceItem> Accounts { get; } = new();
        public List<AccountAnalyticsLinkDefinition> Definitions { get; } = new();

        public static async Task<AccountAnalyticsRegistry> LoadAsync(MetadataService metadataService)
        {
            var registry = new AccountAnalyticsRegistry();
            var catalogs = await metadataService.GetCatalogsAsync();
            var chartCatalog = catalogs.FirstOrDefault(c =>
                string.Equals(c.Name, "План счетов", StringComparison.OrdinalIgnoreCase));

            if (chartCatalog == null)
                return registry;

            registry.Definitions.AddRange(await LoadDefinitionsAsync(metadataService, catalogs));

            var accountRows = await metadataService.GetCatalogDataAsync(chartCatalog.Id);
            foreach (var row in accountRows)
            {
                if (!TryGetGuid(row.GetValueOrDefault("Id"), out var id))
                    continue;

                var code = row.GetValueOrDefault("Код")?.ToString();
                if (string.IsNullOrWhiteSpace(code))
                    code = row.GetValueOrDefault("code")?.ToString();

                if (string.IsNullOrWhiteSpace(code))
                    continue;

                var name = row.GetValueOrDefault("Наименование")?.ToString() ??
                           row.GetValueOrDefault("name")?.ToString() ??
                           string.Empty;

                var account = new AccountReferenceItem
                {
                    Id = id,
                    Code = code,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? code : $"{code} - {name}",
                    ClosingModule = row.GetValueOrDefault("Закрывает модуль")?.ToString() ??
                                    row.GetValueOrDefault("closing_module_code")?.ToString() ??
                                    string.Empty
                };

                var settings = new AccountAnalyticsSettings();
                foreach (var definition in registry.Definitions.Where(definition => definition.IsActive))
                {
                    if (IsFlagEnabled(row.GetValueOrDefault(definition.AccountFlagField)))
                        settings.Enable(definition.Code);
                }

                registry.Accounts.Add(account);
                registry._settingsById[id] = settings;
                registry._settingsByCode[code] = settings;
            }

            return registry;
        }

        public AccountAnalyticsSettings? GetSettings(AccountReferenceItem? account)
        {
            if (account == null)
                return null;

            if (_settingsById.TryGetValue(account.Id, out var byId))
                return byId;

            return _settingsByCode.TryGetValue(account.Code, out var byCode) ? byCode : null;
        }

        public AccountAnalyticsSettings? GetSettingsById(Guid id)
        {
            return _settingsById.TryGetValue(id, out var settings) ? settings : null;
        }

        public AccountAnalyticsSettings? GetSettingsByCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            return _settingsByCode.TryGetValue(NormalizeAccountCode(code), out var settings)
                ? settings
                : null;
        }

        public AccountReferenceItem? FindAccount(object? value)
        {
            if (value == null)
                return null;

            if (TryGetGuid(value, out var id))
            {
                var byId = Accounts.FirstOrDefault(account => account.Id == id);
                if (byId != null)
                    return byId;
            }

            var code = NormalizeAccountCode(value.ToString());
            return Accounts.FirstOrDefault(account =>
                string.Equals(account.Code, code, StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<AccountReferenceItem> GetAccountsForModule(string? moduleCodeOrName)
        {
            if (string.IsNullOrWhiteSpace(moduleCodeOrName))
                return Accounts;

            return Accounts
                .Where(account => ChartOfAccountsSelectionMetadata.IsAccountAllowedForModule(
                    account.ClosingModule,
                    moduleCodeOrName))
                .ToList();
        }

        public AccountAnalyticsSettings? GetSettingsFromValue(object? value)
        {
            var account = FindAccount(value);
            if (account != null)
                return GetSettings(account);

            return GetSettingsByCode(value?.ToString());
        }

        private static async Task<List<AccountAnalyticsLinkDefinition>> LoadDefinitionsAsync(
            MetadataService metadataService,
            List<MetadataObject> catalogs)
        {
            var linkCatalog = catalogs.FirstOrDefault(c =>
                string.Equals(c.Name, AccountAnalyticsCatalogNames.CatalogName, StringComparison.OrdinalIgnoreCase));

            if (linkCatalog == null)
                return AccountAnalyticsDefaultLinks.Items.ToList();

            var rows = await metadataService.GetCatalogDataAsync(linkCatalog.Id);
            var definitions = rows
                .Select(row => new AccountAnalyticsLinkDefinition
                {
                    Code = row.GetValueOrDefault("Код")?.ToString() ?? string.Empty,
                    Name = row.GetValueOrDefault("Наименование")?.ToString() ?? string.Empty,
                    AccountFlagField = row.GetValueOrDefault("Поле настройки счета")?.ToString() ?? string.Empty,
                    ReferenceCatalog = row.GetValueOrDefault("Справочник")?.ToString() ?? string.Empty,
                    DocumentFields = row.GetValueOrDefault("Поля документа")?.ToString() ?? string.Empty,
                    Description = row.GetValueOrDefault("Описание")?.ToString() ?? string.Empty,
                    IsActive = IsFlagEnabled(row.GetValueOrDefault("Активен"))
                })
                .Where(definition =>
                    !string.IsNullOrWhiteSpace(definition.Code) &&
                    !string.IsNullOrWhiteSpace(definition.AccountFlagField))
                .ToList();

            return definitions.Count > 0
                ? definitions
                : AccountAnalyticsDefaultLinks.Items.ToList();
        }

        private static string NormalizeAccountCode(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var text = value.Trim();
            var separatorIndex = text.IndexOf(" - ", StringComparison.Ordinal);
            return separatorIndex > 0 ? text[..separatorIndex].Trim() : text;
        }

        private static bool TryGetGuid(object? value, out Guid guid)
        {
            guid = Guid.Empty;

            if (value is Guid existingGuid)
            {
                guid = existingGuid;
                return true;
            }

            return Guid.TryParse(value?.ToString(), out guid);
        }

        private static bool IsFlagEnabled(object? value)
        {
            return value switch
            {
                true => true,
                string text => text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               text.Equals("да", StringComparison.OrdinalIgnoreCase) ||
                               text == "+",
                _ => false
            };
        }
    }

    public static class AccountAnalyticsRules
    {
        public static bool ShouldShowFieldForRows(
            string fieldName,
            IEnumerable<Dictionary<string, object>> rows,
            IEnumerable<string> accountFieldNames,
            AccountAnalyticsRegistry registry,
            string? referenceCatalog = null)
        {
            var accountNames = accountFieldNames.ToList();
            var settings = rows
                .SelectMany(row => accountNames
                    .Where(row.ContainsKey)
                    .Select(name => registry.GetSettingsFromValue(row[name])))
                .Where(setting => setting != null)
                .Distinct()
                .ToList();

            return ShouldShowField(
                fieldName,
                settings,
                registry.Definitions,
                referenceCatalog,
                showWhenNoAccountSelected: false,
                showUnmappedFields: false);
        }

        public static bool IsAccountSelectorField(MetadataField field)
        {
            if (field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var normalizedName = NormalizeFieldName(field.Name);
            var normalizedColumn = NormalizeFieldName(field.DbColumnName);
            if (normalizedName is "дебет" or "кредит" or "счет дебета" or "счет кредита" or
                "корр счет" or "коррсчет" or "счет кассы" or "счет учета" ||
                normalizedColumn is "debit account" or "credit account" or "corr account" or
                "correspondent account" or "cash account")
            {
                return true;
            }
            if (field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true)
                return true;

            var name = NormalizeFieldName(field.Name);
            return name is "дебет" or "кредит" or "корр счет" or "коррсчет" or "счет кассы" or "счет учета";
        }

        public static bool ShouldShowField(
            MetadataField field,
            IEnumerable<AccountAnalyticsSettings?> selectedSettings,
            IEnumerable<AccountAnalyticsLinkDefinition> definitions,
            bool showWhenNoAccountSelected = true,
            bool showUnmappedFields = true)
        {
            var matchedDefinitions = definitions.Where(definition => definition.Matches(field)).ToList();
            return ShouldShow(matchedDefinitions, selectedSettings, showWhenNoAccountSelected, showUnmappedFields);
        }

        public static bool ShouldShowField(
            string fieldName,
            IEnumerable<AccountAnalyticsSettings?> selectedSettings,
            IEnumerable<AccountAnalyticsLinkDefinition> definitions,
            string? referenceCatalog = null,
            bool showWhenNoAccountSelected = true,
            bool showUnmappedFields = true)
        {
            var matchedDefinitions = definitions
                .Where(definition => definition.Matches(fieldName, referenceCatalog))
                .ToList();

            return ShouldShow(matchedDefinitions, selectedSettings, showWhenNoAccountSelected, showUnmappedFields);
        }

        public static bool IsAccountControlledField(
            MetadataField field,
            IEnumerable<AccountAnalyticsLinkDefinition> definitions)
        {
            return definitions.Any(definition => definition.Matches(field));
        }

        public static object GetEmptyValue(MetadataField field)
        {
            return field.FieldType switch
            {
                "Bool" => false,
                "Int" => 0,
                "Decimal" => 0m,
                "DateTime" => DateTime.Today,
                _ => string.Empty
            };
        }

        public static object GetAccountValueForField(MetadataField field, AccountReferenceItem account)
        {
            if (field.ReferenceCatalog?.StartsWith("План счетов", StringComparison.OrdinalIgnoreCase) == true)
                return account.Code;

            var normalizedName = NormalizeFieldName(field.Name);
            var normalizedColumn = NormalizeFieldName(field.DbColumnName);
            if (normalizedName is "дебет" or "кредит" or "счет дебета" or "счет кредита" or
                "корр счет" or "коррсчет" or "счет кассы" or "счет учета" or
                "счет амортизации" or "затратный счет" or "новый затратный счет" ||
                normalizedColumn is "debit account" or "credit account" or "corr account" or
                "correspondent account" or "cash account" or "asset account" or
                "depreciation account" or "expense account" or "new expense account")
            {
                return account.Code;
            }

            return account.Id.ToString();
        }

        private static bool ShouldShow(
            List<AccountAnalyticsLinkDefinition> matchedDefinitions,
            IEnumerable<AccountAnalyticsSettings?> selectedSettings,
            bool showWhenNoAccountSelected,
            bool showUnmappedFields)
        {
            if (matchedDefinitions.Count == 0)
                return showUnmappedFields;

            var settings = selectedSettings.Where(setting => setting != null).ToList();
            if (settings.Count == 0)
                return showWhenNoAccountSelected;

            return settings.Any(setting => matchedDefinitions.Any(definition => setting!.Allows(definition)));
        }

        private static string NormalizeFieldName(string value)
        {
            return value
                .Replace(".", string.Empty)
                .Replace("-", " ")
                .Replace("_", " ")
                .Trim()
                .ToLowerInvariant();
        }
    }
}
