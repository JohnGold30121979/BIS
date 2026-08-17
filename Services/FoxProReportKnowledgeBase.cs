using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    internal static class FoxProReportKnowledgeBase
    {
        private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> CanonicalAliases =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["operation_name"] = new[] { "name_kod", "naim", "name", "operation", "material_name", "tex", "text" },
                ["debit_account"] = new[] { "schet", "deb", "debet", "debit", "debit_account" },
                ["credit_account"] = new[] { "kor_sch", "korsch", "cred", "credit", "credit_account" },
                ["debit_amount"] = new[] { "debsum", "sum_deb", "deb_beg", "deb_v", "debit_amount" },
                ["credit_amount"] = new[] { "credsum", "sum_cred", "cred_beg", "cred_v", "credit_amount" },
                ["document_number"] = new[] { "dok", "dokum", "nom_dok", "doc", "document", "document_number" },
                ["document_date"] = new[] { "date", "datobr", "doc_date", "document_date" },
                ["module"] = new[] { "module", "prs", "kod_arm" },
                ["organization_name"] = new[] { "organization", "org_name", "name_org", "naim_org", "naim_orgp" },
                ["period_start"] = new[] { "dtb", "dtbeg", "date_begin", "period_start" },
                ["period_end"] = new[] { "dtend", "date_end", "period_end" },
                ["report_title"] = new[] { "sha", "sha1", "report_title", "title" },
                ["report_summary"] = new[] { "sha2", "report_summary", "summary" }
            };

        public static IReadOnlyList<string> CommonDatasetPrefixes { get; } =
        [
            string.Empty,
            "ved.",
            "ved_",
            "ved2.",
            "ved2_",
            "ved3.",
            "ved3_",
            "ved4.",
            "ved4_",
            "db_cr.",
            "db_cr_",
            "db_crs.",
            "db_crs_",
            "dbcr.",
            "dbcr_",
            "dbcrs.",
            "dbcrs_",
            "ksprorg.",
            "ksprorg_",
            "avt_p.",
            "avt_p_"
        ];

        public static IReadOnlyList<string> GetRowDatasetPrefixes(int rowNumber)
        {
            var rowPrefixes = new[]
            {
                $"line{rowNumber}_",
                $"line{rowNumber}.",
                $"ved{rowNumber}.",
                $"ved{rowNumber}_",
                $"db_cr{rowNumber}.",
                $"db_cr{rowNumber}_"
            };

            return rowPrefixes.Concat(CommonDatasetPrefixes).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static void AddAliases(
            Action<string, object?> add,
            IEnumerable<string> prefixes,
            string canonicalName,
            object? value)
        {
            var aliases = GetAliases(canonicalName);
            foreach (var prefix in prefixes)
            foreach (var alias in aliases)
                add(prefix + alias, value);
        }

        public static IReadOnlyList<string> GetAliases(string canonicalName)
        {
            return CanonicalAliases.TryGetValue(canonicalName, out var aliases)
                ? aliases
                : new[] { canonicalName };
        }

        public static string NormalizeRuleSource(string value)
        {
            var source = (value ?? string.Empty).Trim().Trim('{', '}', '=', '"', '\'', ' ');
            if (string.IsNullOrWhiteSpace(source))
                return string.Empty;

            for (var depth = 0; depth < 8; depth++)
            {
                var match = Regex.Match(source, @"(?i)^(?:alltrim|alltr|trim|dtoc|ctod|transform|tran)\((.+)\)$");
                if (!match.Success)
                    break;

                source = match.Groups[1].Value.Trim();
            }

            var datasetMatch = Regex.Match(source,
                @"(?i)\b(?:fact|curFACTSW|irfactsw|ved\d*|db_crs?|dbcrs?|ksprorg|avt_p)[._][A-Za-z0-9_]+\b");
            if (datasetMatch.Success)
                source = datasetMatch.Value;

            return source.Trim().Trim('{', '}', '=', '"', '\'', ' ');
        }

        public static string NormalizeLookupKey(string value)
        {
            return Regex.Replace(NormalizeRuleSource(value).ToLowerInvariant(), @"[\s\.\-]+", "_");
        }

        public static string StripDatasetPrefix(string value)
        {
            var source = NormalizeRuleSource(value);
            var dotIndex = source.LastIndexOf('.');
            if (dotIndex >= 0 && dotIndex < source.Length - 1)
                return source[(dotIndex + 1)..];

            var underscoreMatch = Regex.Match(source, @"(?i)^(?:ved\d*|db_crs?|dbcrs?|ksprorg|avt_p)_(.+)$");
            return underscoreMatch.Success ? underscoreMatch.Groups[1].Value : source;
        }

        public static string GetCanonicalFieldForSource(string source)
        {
            var normalizedSource = NormalizeLookupKey(StripDatasetPrefix(source));
            foreach (var pair in CanonicalAliases)
            {
                if (pair.Value.Any(alias => NormalizeLookupKey(alias) == normalizedSource))
                    return pair.Key;
            }

            return string.Empty;
        }

        public static IReadOnlyList<string> GetTargetFieldCandidates(string canonicalName)
        {
            return canonicalName switch
            {
                "operation_name" => new[]
                {
                    "Наименование материала, вид операции", "Наименование", "Операция", "operation_name", "name_kod"
                },
                "debit_account" => new[] { "Дебет", "debit_account", "schet", "deb" },
                "credit_account" => new[] { "Кредит", "credit_account", "kor_sch", "korsch", "cred" },
                "debit_amount" => new[] { "Сумма Дт", "Дебет сумма", "debit_amount", "debsum" },
                "credit_amount" => new[] { "Сумма Кт", "Кредит сумма", "credit_amount", "credsum" },
                "document_number" => new[] { "N докум", "Документ", "Номер", "document_number", "nom_dok" },
                "document_date" => new[] { "Дата", "date", "datobr", "document_date" },
                "module" => new[] { "Модуль", "module", "prs", "kod_arm" },
                "organization_name" => new[] { "Организация", "organization", "organization_name" },
                "period_start" => new[] { "Период с", "period_start", "dtb", "dtbeg" },
                "period_end" => new[] { "Период по", "period_end", "dtend" },
                "report_title" => new[] { "Заголовок отчета", "report_title", "title" },
                "report_summary" => new[] { "Итоговое описание", "report_summary", "summary" },
                _ => GetAliases(canonicalName)
            };
        }

        public static IReadOnlyList<FoxProReportFieldRule> GetDefaultRules()
        {
            var rules = new List<FoxProReportFieldRule>();
            void Add(string source, string canonical, string target, string display, int priority = 40, string profileCode = "common")
            {
                rules.Add(new FoxProReportFieldRule
                {
                    ProfileCode = profileCode,
                    SourcePattern = NormalizeRuleSource(source),
                    CanonicalField = canonical,
                    TargetFieldName = target,
                    TargetDisplayName = display,
                    IsRegex = false,
                    IsActive = true,
                    Priority = priority,
                    Description = "Предустановленное правило распознавания FoxPro-поля."
                });
            }

            Add("name_kod", "operation_name", "operation_name", "Акт сверки - наименование операции");
            Add("naim", "operation_name", "operation_name", "Акт сверки - наименование операции");
            Add("tex", "operation_name", "operation_name", "Акт сверки - наименование операции");
            Add("text", "operation_name", "operation_name", "Акт сверки - наименование операции");
            Add("schet", "debit_account", "debit_account", "Акт сверки - дебет");
            Add("deb", "debit_account", "debit_account", "Акт сверки - дебет");
            Add("korsch", "credit_account", "credit_account", "Акт сверки - кредит");
            Add("kor_sch", "credit_account", "credit_account", "Акт сверки - кредит");
            Add("cred", "credit_account", "credit_account", "Акт сверки - кредит");
            Add("debsum", "debit_amount", "debit_amount", "Акт сверки - сумма Дт");
            Add("sum_deb", "debit_amount", "debit_amount", "Акт сверки - сумма Дт");
            Add("deb_beg", "debit_amount", "debit_amount", "Акт сверки - сумма Дт");
            Add("deb_v", "debit_amount", "debit_amount", "Акт сверки - сумма Дт");
            Add("debsum_v", "debit_amount", "debit_amount", "Акт сверки - сумма Дт");
            Add("credsum", "credit_amount", "credit_amount", "Акт сверки - сумма Кт");
            Add("sum_cred", "credit_amount", "credit_amount", "Акт сверки - сумма Кт");
            Add("cred_beg", "credit_amount", "credit_amount", "Акт сверки - сумма Кт");
            Add("cred_v", "credit_amount", "credit_amount", "Акт сверки - сумма Кт");
            Add("credsum_v", "credit_amount", "credit_amount", "Акт сверки - сумма Кт");
            Add("nom_dok", "document_number", "document_number", "Акт сверки - номер документа");
            Add("dok", "document_number", "document_number", "Акт сверки - номер документа");
            Add("dokum", "document_number", "document_number", "Акт сверки - номер документа");
            Add("date", "document_date", "document_date", "Акт сверки - дата операции");
            Add("datobr", "document_date", "document_date", "Акт сверки - дата операции");
            Add("prs", "module", "module", "Модуль");
            Add("kod_arm", "module", "module", "Модуль");
            Add("module", "module", "module", "Модуль");
            Add("dtb", "period_start", "period_start", "Период с");
            Add("dtbeg", "period_start", "period_start", "Период с");
            Add("dtend", "period_end", "period_end", "Период по");
            Add("sha", "report_title", "report_title", "Заголовок отчета", 60);
            Add("sha1", "report_title", "report_title", "Заголовок отчета", 60);
            Add("sha2", "report_summary", "report_summary", "Итоговое описание", 60);
            Add("ksprorg.naim_orgp", "organization_name", "organization", "Организация", 45);

            Add("fact.NOM_BL", "invoice_number", "fact.NOM_BL", "Счет-фактура - номер", 45, "foxpro_invoice_kg");
            Add("fact.SER_BL", "invoice_series", "fact.SER_BL", "Счет-фактура - серия/ЭСФ", 45, "foxpro_invoice_kg");
            Add("fact.D_SALE", "invoice_date", "fact.D_SALE", "Счет-фактура - дата", 45, "foxpro_invoice_kg");
            Add("fact.TXT_KOR", "invoice_basis", "fact.TXT_KOR", "Счет-фактура - основание", 45, "foxpro_invoice_kg");
            Add("fact.C_NAME_ORG", "issuer_name", "fact.C_NAME_ORG", "Организация А - наименование", 45, "foxpro_invoice_kg");
            Add("fact.C_INN", "issuer_inn", "fact.C_INN", "Организация А - ИНН", 45, "foxpro_invoice_kg");
            Add("fact.C_OKPO", "issuer_okpo", "fact.C_OKPO", "Организация А - ОКПО", 45, "foxpro_invoice_kg");
            Add("fact.C_ADR", "issuer_address", "fact.C_ADR", "Организация А - адрес", 45, "foxpro_invoice_kg");
            Add("fact.C_PHONE", "issuer_phone", "fact.C_PHONE", "Организация А - телефон", 45, "foxpro_invoice_kg");
            Add("fact.C_BANK", "issuer_bank", "fact.C_BANK", "Организация А - банк", 45, "foxpro_invoice_kg");
            Add("fact.C_RS", "issuer_account", "fact.C_RS", "Организация А - расчетный счет", 45, "foxpro_invoice_kg");
            Add("fact.C_BIK", "issuer_bik", "fact.C_BIK", "Организация А - БИК", 45, "foxpro_invoice_kg");
            Add("fact.C_DIR", "issuer_director", "fact.C_DIR", "Организация А - руководитель", 45, "foxpro_invoice_kg");
            Add("fact.C_BUH", "issuer_accountant", "fact.C_BUH", "Организация А - главный бухгалтер", 45, "foxpro_invoice_kg");
            Add("fact.D_NAME_ORG", "recipient_name", "fact.D_NAME_ORG", "Организация Б - наименование", 45, "foxpro_invoice_kg");
            Add("fact.D_INN", "recipient_inn", "fact.D_INN", "Организация Б - ИНН", 45, "foxpro_invoice_kg");
            Add("fact.D_OKPO", "recipient_okpo", "fact.D_OKPO", "Организация Б - ОКПО", 45, "foxpro_invoice_kg");
            Add("fact.D_ADR", "recipient_address", "fact.D_ADR", "Организация Б - адрес", 45, "foxpro_invoice_kg");
            Add("fact.D_PHONE", "recipient_phone", "fact.D_PHONE", "Организация Б - телефон", 45, "foxpro_invoice_kg");
            Add("fact.D_BANK", "recipient_bank", "fact.D_BANK", "Организация Б - банк", 45, "foxpro_invoice_kg");
            Add("fact.D_RS", "recipient_account", "fact.D_RS", "Организация Б - расчетный счет", 45, "foxpro_invoice_kg");
            Add("fact.D_BIK", "recipient_bik", "fact.D_BIK", "Организация Б - БИК", 45, "foxpro_invoice_kg");
            Add("fact.D_DIR", "recipient_director", "fact.D_DIR", "Организация Б - руководитель", 45, "foxpro_invoice_kg");
            Add("fact.D_BUH", "recipient_accountant", "fact.D_BUH", "Организация Б - главный бухгалтер", 45, "foxpro_invoice_kg");

            return rules;
        }
        public static bool RuleMatches(FoxProReportFieldRule rule, string expression)
        {
            if (string.IsNullOrWhiteSpace(rule.SourcePattern))
                return false;

            var source = NormalizeRuleSource(expression);
            if (rule.IsRegex)
            {
                try
                {
                    return Regex.IsMatch(source, rule.SourcePattern, RegexOptions.IgnoreCase) ||
                           Regex.IsMatch(expression ?? string.Empty, rule.SourcePattern, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            var ruleSource = NormalizeRuleSource(rule.SourcePattern);
            return string.Equals(source, ruleSource, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeLookupKey(source), NormalizeLookupKey(ruleSource), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(NormalizeLookupKey(StripDatasetPrefix(source)), NormalizeLookupKey(StripDatasetPrefix(ruleSource)), StringComparison.OrdinalIgnoreCase);
        }

        public static bool LooksLikeKnownDatasetField(string value)
        {
            var text = (value ?? string.Empty).Trim();
            return Regex.IsMatch(text,
                @"(?i)\b(?:fact|curFACTSW|irfactsw|ved\d*|db_crs?|dbcrs?|ksprorg|avt_p)[._][A-Za-z0-9_]+\b");
        }
    }
}