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
        "            SourceName: \"Проводки\",",
        "            SourceObjectType: \"Document\",",
        "            ReportType: \"FoxProLayout\",",
        "            Icon: \"🤝\",",
        $"            Order: {variant.Order},",
        "            PageOrientation: \"Landscape\",",
        "            IsPrintForm: false,",
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
    @"\s*new\(\s*Code:\s*""standard\.frx\.finance\.reconciliation\.[\s\S]*?TemplateCompressedBase64:\s*""[^""]*""\),\r?\n",
    string.Empty);

var insertion = string.Join(Environment.NewLine, blocks) + Environment.NewLine;
var marker = "        new(\r\n            Code: \"standard.frx.fixed-assets.statement\"";
if (!text.Contains(marker))
    marker = "        new(\n            Code: \"standard.frx.fixed-assets.statement\"";
if (!text.Contains(marker))
    throw new InvalidOperationException("Точка вставки перед FRX ОС не найдена.");

File.WriteAllText(outputFile, text.Replace(marker, insertion + marker), new UTF8Encoding(false));
Console.WriteLine($"Added reconciliation FRX metadata definitions: {blocks.Count}");
return 0;

internal sealed record ReportVariant(
    string FileName,
    string Code,
    string Name,
    string Description,
    int Order,
    bool IsDefault);

