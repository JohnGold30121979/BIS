using BIS.ERP.Data;
using BIS.ERP.Models;
using BIS.ERP.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

// Проверка реального форматирования EF Core без подключения к базе.
using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
    .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused").Options);
var builder = context.GetService<IRawSqlCommandBuilder>();
try
{
    builder.Build("SELECT '{}'", Array.Empty<object>());
    throw new Exception("Не удалось воспроизвести исходный FormatException");
}
catch (FormatException) { }
foreach (var table in new[] { "doc_sales_invoice_lines", "doc_purchase_invoice_lines", "doc_advance_payment_lines" })
{
    var sql = $"ALTER TABLE {table} ADD COLUMN IF NOT EXISTS line_analytics text NOT NULL DEFAULT {LineAnalytics.EmptySqlDefault};";
    if (builder.Build(sql, Array.Empty<object>()).RelationalCommand.CommandText != sql)
        throw new Exception("SQL изменился при форматировании");
}
var inventoryColumn = InventoryDocumentModel.LineColumns.Single(item => item.Column == "line_analytics").Definition;
builder.Build($"CREATE TABLE test_lines (line_analytics {inventoryColumn});", Array.Empty<object>());
builder.Build($"ALTER TABLE test_lines ADD COLUMN IF NOT EXISTS line_analytics {inventoryColumn};", Array.Empty<object>());
Console.WriteLine("PASS: исходное исключение воспроизведено; SQL счет-фактур, авансов и ТМЗ проходит EF Core RawSqlCommandBuilder.");
