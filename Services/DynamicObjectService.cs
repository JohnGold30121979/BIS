using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class DynamicObjectService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;

        public DynamicObjectService(AppDbContext context, MetadataService metadataService)
        {
            _context = context;
            _metadataService = metadataService;
        }

        /// <summary>
        /// Получить данные объекта по метаданным
        /// </summary>
        public async Task<List<Dictionary<string, object>>> GetDataAsync(Guid metadataObjectId)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Fields)
                .FirstOrDefaultAsync(m => m.Id == metadataObjectId);

            if (metadata == null) return new List<Dictionary<string, object>>();

            return await _metadataService.GetCatalogDataAsync(metadataObjectId);
        }

        /// <summary>
        /// Выполнить расчеты для объекта (амортизация, итоги и т.д.)
        /// </summary>
        public async Task ExecuteCalculationsAsync(Guid metadataObjectId, Guid recordId)
        {
            var metadata = await _context.MetadataObjects
                .Include(m => m.Calculations)
                .FirstOrDefaultAsync(m => m.Id == metadataObjectId);

            if (metadata == null || !metadata.Calculations.Any()) return;

            var data = await GetRecordDataAsync(metadata.TableName, recordId);

            foreach (var calc in metadata.Calculations.OrderBy(c => c.ExecutionOrder))
            {
                var value = await CalculateValueAsync(calc, data);
                await UpdateRecordFieldAsync(metadata.TableName, recordId, calc.TargetField, value);
            }
        }

        /// <summary>
        /// Создать проводки по правилам
        /// </summary>
        public async Task<List<DynamicPosting>> GeneratePostingsAsync(Guid metadataObjectId, Guid recordId)
        {
            var postings = new List<DynamicPosting>();

            var metadata = await _context.MetadataObjects
                .Include(m => m.PostingRules)
                .FirstOrDefaultAsync(m => m.Id == metadataObjectId);

            if (metadata == null || !metadata.PostingRules.Any()) return postings;

            var data = await GetRecordDataAsync(metadata.TableName, recordId);

            foreach (var rule in metadata.PostingRules.OrderBy(r => r.Order))
            {
                if (!string.IsNullOrEmpty(rule.Condition))
                {
                    var conditionMet = await EvaluateConditionAsync(rule.Condition, data);
                    if (!conditionMet) continue;
                }

                var posting = new DynamicPosting
                {
                    Id = Guid.NewGuid(),
                    ObjectId = recordId,
                    ObjectType = metadata.Name,
                    Date = data.ContainsKey("Date") ? Convert.ToDateTime(data["Date"]) : DateTime.Now,
                    DebitAccount = EvaluateExpression(rule.DebitAccountExpression, data),
                    CreditAccount = EvaluateExpression(rule.CreditAccountExpression, data),
                    Amount = Convert.ToDecimal(EvaluateExpression(rule.AmountExpression, data)),
                    CreatedAt = DateTime.Now
                };

                postings.Add(posting);
            }

            return postings;
        }

        // Вспомогательные методы
        private async Task<Dictionary<string, object>> GetRecordDataAsync(string tableName, Guid recordId)
        {
            var sql = $"SELECT * FROM \"{tableName}\" WHERE \"Id\" = '{recordId}'";
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            await _context.Database.OpenConnectionAsync();
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                var result = new Dictionary<string, object>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    result[reader.GetName(i)] = reader.GetValue(i);
                }
                await _context.Database.CloseConnectionAsync();
                return result;
            }

            await _context.Database.CloseConnectionAsync();
            return new Dictionary<string, object>();
        }

        private async Task<object> CalculateValueAsync(MetadataCalculation calc, Dictionary<string, object> data)
        {
            return calc.CalculationType switch
            {
                "Depreciation" => await CalculateDepreciationAsync(calc, data),
                "Sum" => CalculateSum(calc, data),
                "Formula" => EvaluateFormula(calc.Formula, data),
                _ => 0
            };
        }

        private async Task<decimal> CalculateDepreciationAsync(MetadataCalculation calc, Dictionary<string, object> data)
        {
            // Линейная амортизация
            var initialCost = GetDecimalValue(data, "initial_cost", "InitialCost", "Первоначальная стоимость");
            var usefulLife = GetIntValue(data, "useful_life_months", "UsefulLife", "useful_life", "Срок полезного использования, мес.");
            var depreciationRate = GetDecimalValue(data, "depreciation_rate", "DepreciationRate", "Норма амортизации, %");

            if (usefulLife > 0)
                return initialCost / usefulLife;
            if (depreciationRate > 0)
                return initialCost * depreciationRate / 100;

            return 0;
        }

        private static decimal GetDecimalValue(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var value) &&
                    value != null &&
                    value != DBNull.Value &&
                    decimal.TryParse(value.ToString(), out var result))
                {
                    return result;
                }
            }

            return 0m;
        }

        private static int GetIntValue(Dictionary<string, object> data, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (data.TryGetValue(key, out var value) &&
                    value != null &&
                    value != DBNull.Value &&
                    int.TryParse(value.ToString(), out var result))
                {
                    return result;
                }
            }

            return 0;
        }

        private decimal CalculateSum(MetadataCalculation calc, Dictionary<string, object> data)
        {
            if (string.IsNullOrEmpty(calc.SourceFields)) return 0;

            var fields = System.Text.Json.JsonSerializer.Deserialize<List<string>>(calc.SourceFields);
            decimal sum = 0;

            foreach (var field in fields)
            {
                if (data.ContainsKey(field))
                    sum += Convert.ToDecimal(data[field]);
            }

            return sum;
        }

        private decimal EvaluateFormula(string formula, Dictionary<string, object> data)
        {
            // Простой evaluator формул
            var result = formula;
            foreach (var kvp in data)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "0");
            }

            using var table = new System.Data.DataTable();
            return Convert.ToDecimal(table.Compute(result, ""));
        }

        private async Task<bool> EvaluateConditionAsync(string condition, Dictionary<string, object> data)
        {
            // Простой evaluator условий
            var result = condition;
            foreach (var kvp in data)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "null");
            }

            using var table = new System.Data.DataTable();
            return Convert.ToBoolean(table.Compute(result, ""));
        }

        private string EvaluateExpression(string expression, Dictionary<string, object> data)
        {
            var result = expression;
            foreach (var kvp in data)
            {
                result = result.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "");
            }
            return result;
        }

        private async Task UpdateRecordFieldAsync(string tableName, Guid recordId, string fieldName, object value)
        {
            var sql = $"UPDATE \"{tableName}\" SET \"{fieldName}\" = @value, \"UpdatedAt\" = NOW() WHERE \"Id\" = '{recordId}'";
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            var param = command.CreateParameter();
            param.ParameterName = "@value";
            param.Value = value ?? DBNull.Value;
            command.Parameters.Add(param);

            await _context.Database.OpenConnectionAsync();
            await command.ExecuteNonQueryAsync();
            await _context.Database.CloseConnectionAsync();
        }
    }

    // Универсальная проводка
    public class DynamicPosting
    {
        public Guid Id { get; set; }
        public Guid ObjectId { get; set; }
        public string ObjectType { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string DebitAccount { get; set; } = string.Empty;
        public string CreditAccount { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
