using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public static class ReferenceDisplayHelper
    {
        public static async Task<Dictionary<string, Dictionary<Guid, string>>> LoadMapsAsync(
            MetadataObject metadata,
            MetadataService metadataService)
        {
            var result = new Dictionary<string, Dictionary<Guid, string>>(StringComparer.OrdinalIgnoreCase);
            var catalogs = await metadataService.GetCatalogsAsync();
            var catalogsByName = catalogs.ToDictionary(catalog => catalog.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var field in metadata.Fields.Where(field =>
                         field.FieldType == "Reference" &&
                         !string.IsNullOrWhiteSpace(field.ReferenceCatalog)))
            {
                if (!catalogsByName.TryGetValue(field.ReferenceCatalog!, out var catalog))
                    continue;

                var rows = await metadataService.GetCatalogDataAsync(catalog.Id);
                result[field.Name] = rows
                    .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                    .ToDictionary(
                        row => Guid.Parse(row["Id"].ToString()!),
                        row => BuildDisplayValue(row, field));
            }

            return result;
        }

        public static List<Dictionary<string, object>> ResolveRows(
            IEnumerable<Dictionary<string, object>> rows,
            IReadOnlyDictionary<string, Dictionary<Guid, string>> referenceMaps)
        {
            return rows.Select(row =>
            {
                var resolved = new Dictionary<string, object>(row);
                foreach (var (fieldName, map) in referenceMaps)
                {
                    if (!resolved.TryGetValue(fieldName, out var rawValue) ||
                        !TryGetGuid(rawValue, out var id))
                    {
                        continue;
                    }

                    if (map.TryGetValue(id, out var displayValue))
                        resolved[fieldName] = displayValue;
                }

                return resolved;
            }).ToList();
        }

        public static string BuildDisplayValue(Dictionary<string, object> row, MetadataField field)
        {
            if (!string.IsNullOrWhiteSpace(field.DisplayPattern))
            {
                var displayValue = field.DisplayPattern;
                var displayFields = (field.DisplayFields ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                foreach (var displayField in displayFields)
                {
                    var value = GetValue(row, displayField)?.ToString() ?? string.Empty;
                    displayValue = displayValue.Replace($"{{{displayField}}}", value);
                }

                var cleaned = displayValue.Trim(' ', '-');
                if (!string.IsNullOrWhiteSpace(cleaned))
                    return cleaned;
            }

            foreach (var name in new[]
                     {
                         "Наименование", "Наименование материала", "Наименование банка",
                         "ФИО", "Счет", "Код", "name", "code"
                     })
            {
                var value = GetValue(row, name)?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return row.GetValueOrDefault("Id")?.ToString() ?? string.Empty;
        }

        private static object? GetValue(Dictionary<string, object> row, string fieldName)
        {
            if (row.TryGetValue(fieldName, out var value))
                return value;

            var normalizedName = fieldName.Replace(" ", "_");
            if (row.TryGetValue(normalizedName, out value))
                return value;

            var match = row.FirstOrDefault(pair =>
                pair.Key.Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
            return match.Equals(default(KeyValuePair<string, object>)) ? null : match.Value;
        }

        private static bool TryGetGuid(object? value, out Guid id)
        {
            if (value is Guid guid)
            {
                id = guid;
                return true;
            }

            return Guid.TryParse(value?.ToString(), out id);
        }
    }
}
