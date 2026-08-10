using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using BIS.ERP.Services;

if (args.Length < 2)
{
    Console.WriteLine("Usage: BIS.ERP.FrxMetadataGenerator <fox-finance-folder> <StandardFrxReportTemplates.generated.cs>");
    return 2;
}

var sourceDirectory = args[0];
var outputFile = args[1];

var variants = new[]
{
    new ReportVariant("prb1.frx", "standard.frx.finance.advance-payments.prb1", "Авансовые платежи (FRX FoxPro)", "FoxPro-макет авансовых платежей prb1.frx.", 1050, true, "Авансовые платежи", true, "Portrait", "💰"),
    new ReportVariant("prb1.frx", "standard.frx.finance.trial-balance.full", "ОСВ полная (FRX FoxPro)", "FoxPro-макет полной оборотно-сальдовой ведомости prb1.frx.", 1041, false, "Проводки", true, "Portrait", "📊"),
    new ReportVariant("pra1k1.frx", "standard.frx.finance.trial-balance.debit-turnovers", "ОСВ обороты по дебету (FRX FoxPro)", "FoxPro-макет оборотов по дебету счета pra1k1.frx.", 1042, false, "Проводки", true, "Landscape", "📊"),
    new ReportVariant("pra1k2.frx", "standard.frx.finance.trial-balance.account-summary", "ОСВ сводные обороты по счету (FRX FoxPro)", "FoxPro-макет сводных оборотов по счету pra1k2.frx.", 1043, false, "Проводки", true, "Landscape", "📊"),
    new ReportVariant("pra1z.frx", "standard.frx.finance.trial-balance.postings", "ОСВ проводки (FRX FoxPro)", "FoxPro-макет проводок по счету pra1z.frx.", 1044, false, "Проводки", true, "Landscape", "📊"),
    new ReportVariant("pr_kasp.frx", "standard.frx.finance.cash.cash-book", "Кассовая книга (FRX FoxPro)", "FoxPro-макет кассовой книги pr_kasp.frx.", 1045, false, "Расходный/Приходный КО", false, "Portrait", "📒"),
    new ReportVariant("pra2.frx", "standard.frx.finance.cash.receipts-expenses-register", "Реестр приходов/расходов (FRX FoxPro)", "FoxPro-макет реестра приходов/расходов pra2.frx.", 1046, false, "Расходный/Приходный КО", false, "Portrait", "📋"),
    new ReportVariant("pr_pl23.frx", "standard.frx.finance.payment-order.pr-pl23", "Платежное поручение pr_pl23 (FRX FoxPro)", "FoxPro-макет платежного поручения pr_pl23.frx.", 1047, true, "Платежное поручение", true, "Landscape", "🏦"),
    new ReportVariant("pr_vzp.frx", "standard.frx.finance.reconciliation.pr-vzp", "Акт сверки (FRX FoxPro)", "Основной FoxPro-макет акта сверки pr_vzp.frx.", 1060, true),
    new ReportVariant("pr_vzp_.frx", "standard.frx.finance.reconciliation.pr-vzp-short", "Акт сверки краткий (FRX FoxPro)", "Краткий FoxPro-макет акта сверки pr_vzp_.frx.", 1061, false),
    new ReportVariant("PR_VZP1.FRX", "standard.frx.finance.reconciliation.pr-vzp1", "Акт сверки вариант 1 (FRX FoxPro)", "Дополнительный FoxPro-макет акта сверки PR_VZP1.FRX.", 1062, false),
    new ReportVariant("pr_vzpa.frx", "standard.frx.finance.reconciliation.advance", "Акт сверки по авансам (FRX FoxPro)", "FoxPro-макет акта сверки по авансовым расчетам pr_vzpa.frx.", 1063, false),
    new ReportVariant("pr_vzpav.frx", "standard.frx.finance.reconciliation.advance-currency", "Акт сверки по авансам с валютой (FRX FoxPro)", "FoxPro-макет валютного акта сверки по авансам pr_vzpav.frx.", 1064, false),
    new ReportVariant("pr_vzpd.frx", "standard.frx.finance.reconciliation.counterparty", "Акт сверки по данным сторон (FRX FoxPro)", "FoxPro-макет акта сверки с колонками нашей стороны и контрагента pr_vzpd.frx.", 1065, false),
    new ReportVariant("pr_vzpd_.frx", "standard.frx.finance.reconciliation.counterparty-short", "Акт сверки по данным сторон краткий (FRX FoxPro)", "Краткий FoxPro-макет акта сверки по данным сторон pr_vzpd_.frx.", 1066, false),
    new ReportVariant("pr_vzpdv.frx", "standard.frx.finance.reconciliation.counterparty-currency", "Акт сверки по данным сторон с валютой (FRX FoxPro)", "FoxPro-макет акта сверки по данным сторон с валютой pr_vzpdv.frx.", 1067, false),
    new ReportVariant("PR_VZPI.FRX", "standard.frx.finance.reconciliation.invoice", "Акт сверки по счет-фактурам (FRX FoxPro)", "FoxPro-макет акта сверки по счет-фактурам PR_VZPI.FRX.", 1068, false),
    new ReportVariant("pr_vzpK.frx", "standard.frx.finance.reconciliation.cash", "Акт сверки по кассе (FRX FoxPro)", "FoxPro-макет акта сверки по кассовым расчетам pr_vzpK.frx.", 1069, false),
    new ReportVariant("pr_vzpm.frx", "standard.frx.finance.reconciliation.materials", "Акт сверки по материалам (FRX FoxPro)", "FoxPro-макет акта сверки по материальным расчетам pr_vzpm.frx.", 1070, false),
    new ReportVariant("pr_vzps.frx", "standard.frx.finance.reconciliation.accounts", "Акт сверки по счетам (FRX FoxPro)", "FoxPro-макет акта сверки по счетам pr_vzps.frx.", 1071, false),
    new ReportVariant("pr_vzps_.frx", "standard.frx.finance.reconciliation.accounts-short", "Акт сверки по счетам краткий (FRX FoxPro)", "Краткий FoxPro-макет акта сверки по счетам pr_vzps_.frx.", 1072, false),
    new ReportVariant("pr_vzpt.frx", "standard.frx.finance.reconciliation.goods", "Акт сверки по товарам (FRX FoxPro)", "FoxPro-макет акта сверки по товарным расчетам pr_vzpt.frx.", 1073, false),
    new ReportVariant("pr_vzpv.frx", "standard.frx.finance.reconciliation.currency", "Акт сверки валютный (FRX FoxPro)", "Основной FoxPro-макет валютного акта сверки pr_vzpv.frx.", 1074, false),
    new ReportVariant("pr_vzpv_.frx", "standard.frx.finance.reconciliation.currency-short", "Акт сверки валютный краткий (FRX FoxPro)", "Краткий FoxPro-макет валютного акта сверки pr_vzpv_.frx.", 1075, false),
    new ReportVariant("pr_vzpvm.frx", "standard.frx.finance.reconciliation.currency-materials", "Акт сверки валютный по материалам (FRX FoxPro)", "FoxPro-макет валютного акта сверки по материалам pr_vzpvm.frx.", 1076, false)
};

static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

static string Compress(string text)
{
    var bytes = Encoding.UTF8.GetBytes(text);
    using var output = new MemoryStream();
    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        gzip.Write(bytes, 0, bytes.Length);
    return Convert.ToBase64String(output.ToArray());
}

static string BuildDefinition(ReportVariant variant, string compressed)
{
    return string.Join(Environment.NewLine, new[]
    {
        "        new(",
        $"            Code: \"{Escape(variant.Code)}\",",
        $"            Name: \"{Escape(variant.Name)}\",",
        $"            Description: \"{Escape(variant.Description)}\",",
        "            ModuleCode: \"Finance\",",
        $"            SourceName: \"{Escape(variant.SourceName)}\",",
        "            SourceObjectType: \"Document\",",
        "            ReportType: \"FoxProLayout\",",
        $"            Icon: \"{Escape(variant.Icon)}\",",
        $"            Order: {variant.Order},",
        $"            PageOrientation: \"{Escape(variant.PageOrientation)}\",",
        $"            IsPrintForm: {(variant.IsPrintForm ? "true" : "false")},",
        $"            IsDefault: {(variant.IsDefault ? "true" : "false")},",
        $"            TemplateCompressedBase64: \"{compressed}\"),"
    });
}

var parser = new FrxParser();
var blocks = new List<string>();
foreach (var variant in variants)
{
    var fullPath = Path.Combine(sourceDirectory, variant.FileName);
    if (!File.Exists(fullPath))
        throw new FileNotFoundException("FRX не найден", fullPath);

    Console.WriteLine($"Converting {variant.FileName}...");
    var parsed = parser.ParseFrxFile(fullPath);
    blocks.Add(BuildDefinition(variant, Compress(parsed.FrxXml)));
}

var text = File.ReadAllText(outputFile, Encoding.UTF8);
text = Regex.Replace(
    text,
    @"\s*new\(\s*Code:\s*""standard\.frx\.finance\.(?:reconciliation\.[^""\r\n]+|advance-payments\.prb1|trial-balance\.[^""\r\n]+|cash\.[^""\r\n]+|payment-order\.[^""\r\n]+)""[\s\S]*?TemplateCompressedBase64:\s*""[^""]*""\),\r?\n",
    string.Empty);

var insertion = string.Join(Environment.NewLine, blocks) + Environment.NewLine;
var marker = "        new(\r\n            Code: \"standard.frx.fixed-assets.statement\"";
if (!text.Contains(marker))
    marker = "        new(\n            Code: \"standard.frx.fixed-assets.statement\"";
if (!text.Contains(marker))
    throw new InvalidOperationException("Точка вставки перед FRX ОС не найдена.");

File.WriteAllText(outputFile, text.Replace(marker, insertion + marker), new UTF8Encoding(false));
Console.WriteLine($"Added finance FRX metadata definitions: {blocks.Count}");
return 0;

internal sealed record ReportVariant(
    string FileName,
    string Code,
    string Name,
    string Description,
    int Order,
    bool IsDefault,
    string SourceName = "Проводки",
    bool IsPrintForm = false,
    string PageOrientation = "Landscape",
    string Icon = "🤝");
