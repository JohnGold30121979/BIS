using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class EmployeeService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;
        private Guid? _catalogId;

        public EmployeeService(AppDbContext context, MetadataService metadataService)
        {
            _context = context;
            _metadataService = metadataService;
        }

        private async Task<Guid> GetCatalogIdAsync()
        {
            if (_catalogId.HasValue) return _catalogId.Value;

            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.Name == "Сотрудники (Списочный состав)" && m.ObjectType == "Catalog");

            _catalogId = catalog?.Id ?? Guid.Empty;
            return _catalogId.Value;
        }

        // Получение всех сотрудников
        public async Task<List<Employee>> GetAllEmployeesAsync()
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) return new List<Employee>();

            var data = await _metadataService.GetCatalogDataAsync(catalogId);
            var catalogs = await _metadataService.GetCatalogsAsync();
            var positionMap = await LoadReferenceMapAsync(catalogs, "Должности");
            var departmentMap = await LoadReferenceMapAsync(catalogs, "Подразделения");
            var employees = new List<Employee>();

            foreach (var row in data)
            {
                var employee = new Employee
                {
                    Id = row.ContainsKey("Id") ? Guid.Parse(row["Id"].ToString()) : Guid.NewGuid(),
                    PersonnelNumber = GetStringValue(row, "Табельный номер"),
                    FullName = GetStringValue(row, "ФИО"),
                    PositionText = GetStringValue(row, "Должность (текст)"),
                    HireDate = GetDateValue(row, "Дата приема"),
                    TerminationDate = GetDateValue(row, "Дата увольнения"),
                    Status = GetStringValue(row, "Статус", "Активен"),
                    Phone = GetStringValue(row, "Телефон"),
                    Email = GetStringValue(row, "Email"),
                    TaxId = GetStringValue(row, "ИНН"),
                    PassportNumber = GetStringValue(row, "Паспорт №/ID"),
                    PassportIssuedBy = GetStringValue(row, "Кем выдан"),
                    PassportIssueDate = GetDateValue(row, "Дата выдачи"),
                    BirthDate = GetDateValue(row, "Дата рождения"),
                    Address = GetStringValue(row, "Адрес"),
                    Notes = GetStringValue(row, "Примечание"),
                    IsActive = GetStringValue(row, "Статус", "Активен") == "Активен"
                };

                // Загружаем PositionId если есть
                if (row.ContainsKey("Должность (справочник)") && row["Должность (справочник)"] != null && row["Должность (справочник)"] != DBNull.Value)
                {
                    if (Guid.TryParse(row["Должность (справочник)"].ToString(), out var positionId))
                    {
                        employee.PositionId = positionId;
                        employee.PositionDisplay = positionMap.GetValueOrDefault(positionId, string.Empty);
                    }
                }

                // Загружаем DepartmentId если есть
                if (row.ContainsKey("Подразделение") && row["Подразделение"] != null && row["Подразделение"] != DBNull.Value)
                {
                    if (Guid.TryParse(row["Подразделение"].ToString(), out var departmentId))
                    {
                        employee.DepartmentId = departmentId;
                        employee.Department = departmentMap.GetValueOrDefault(departmentId, string.Empty);
                    }
                    else
                    {
                        employee.Department = GetStringValue(row, "Подразделение");
                    }
                }

                employees.Add(employee);
            }

            return employees.OrderBy(e => e.PersonnelNumber).ToList();
        }

        // Добавление сотрудника
        public async Task<Employee> AddEmployeeAsync(Employee employee)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) throw new Exception("Справочник сотрудников не найден");

            var data = new Dictionary<string, object>
            {
                ["Код"] = employee.PersonnelNumber,
                ["Наименование"] = employee.FullName,
                ["Табельный номер"] = employee.PersonnelNumber,
                ["ФИО"] = employee.FullName,
                ["Должность (справочник)"] = employee.PositionId ?? (object)DBNull.Value,
                ["Должность (текст)"] = GetPositionTextForStorage(employee),
                ["Подразделение"] = employee.DepartmentId ?? (object)DBNull.Value,
                ["Дата рождения"] = employee.BirthDate ?? (object)DBNull.Value,
                ["Дата приема"] = employee.HireDate ?? (object)DBNull.Value,
                ["Дата увольнения"] = employee.TerminationDate ?? (object)DBNull.Value,
                ["Адрес"] = employee.Address ?? (object)DBNull.Value,
                ["Примечание"] = employee.Notes ?? (object)DBNull.Value,
                ["Статус"] = employee.Status,
                ["Телефон"] = employee.Phone,
                ["Email"] = employee.Email,
                ["ИНН"] = employee.TaxId,
                ["Паспорт №/ID"] = employee.PassportNumber,
                ["Кем выдан"] = ToUpperOrDbNull(employee.PassportIssuedBy),
                ["Дата выдачи"] = employee.PassportIssueDate ?? (object)DBNull.Value
            };

            var recordId = await _metadataService.CreateDynamicRecordAsync(catalogId, data);
            employee.Id = recordId;
            return employee;
        }

        // Обновление сотрудника
        public async Task<Employee> UpdateEmployeeAsync(Employee employee)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) throw new Exception("Справочник сотрудников не найден");

            var data = new Dictionary<string, object>
            {
                ["Код"] = employee.PersonnelNumber,
                ["Наименование"] = employee.FullName,
                ["Табельный номер"] = employee.PersonnelNumber,
                ["ФИО"] = employee.FullName,
                ["Должность (справочник)"] = employee.PositionId ?? (object)DBNull.Value,
                ["Должность (текст)"] = GetPositionTextForStorage(employee),
                ["Подразделение"] = employee.DepartmentId ?? (object)DBNull.Value,
                ["Дата рождения"] = employee.BirthDate ?? (object)DBNull.Value,
                ["Дата приема"] = employee.HireDate ?? (object)DBNull.Value,
                ["Дата увольнения"] = employee.TerminationDate ?? (object)DBNull.Value,
                ["Адрес"] = employee.Address ?? (object)DBNull.Value,
                ["Примечание"] = employee.Notes ?? (object)DBNull.Value,
                ["Статус"] = employee.Status,
                ["Телефон"] = employee.Phone,
                ["Email"] = employee.Email,
                ["ИНН"] = employee.TaxId,
                ["Паспорт №/ID"] = employee.PassportNumber,
                ["Кем выдан"] = ToUpperOrDbNull(employee.PassportIssuedBy),
                ["Дата выдачи"] = employee.PassportIssueDate ?? (object)DBNull.Value
            };

            await _metadataService.UpdateDynamicRecordAsync(catalogId, employee.Id, data);
            return employee;
        }

        // Удаление сотрудника
        public async Task<bool> DeleteEmployeeAsync(Guid id)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) return false;

            await _metadataService.DeleteDynamicRecordAsync(catalogId, id);
            return true;
        }

        // Увольнение сотрудника
        public async Task<Employee> TerminateEmployeeAsync(Guid id, DateTime terminationDate, string reason)
        {
            var employee = (await GetAllEmployeesAsync()).FirstOrDefault(e => e.Id == id);
            if (employee == null) throw new Exception("Сотрудник не найден");

            employee.TerminationDate = terminationDate;
            employee.Status = reason;
            employee.IsActive = false;

            await UpdateEmployeeAsync(employee);
            return employee;
        }

        // Поиск сотрудников
        public async Task<List<Employee>> SearchEmployeesAsync(string searchText)
        {
            var all = await GetAllEmployeesAsync();
            if (string.IsNullOrWhiteSpace(searchText)) return all;

            return all.Where(e =>
                e.PersonnelNumber.Contains(searchText) ||
                e.FullName.Contains(searchText) ||
                e.Position.Contains(searchText) ||
                e.Department.Contains(searchText) ||
                e.PassportNumber.Contains(searchText) ||
                e.PassportIssuedBy.Contains(searchText)
            ).ToList();
        }

        // Экспорт в JSON
        public async Task<string> ExportEmployeesToJsonAsync()
        {
            var employees = await GetAllEmployeesAsync();
            return System.Text.Json.JsonSerializer.Serialize(employees, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        // Импорт из JSON
        public async Task<int> ImportEmployeesFromJsonAsync(string jsonContent)
        {
            var employees = System.Text.Json.JsonSerializer.Deserialize<List<Employee>>(jsonContent);
            if (employees == null) return 0;

            foreach (var emp in employees)
            {
                await AddEmployeeAsync(emp);
            }
            return employees.Count;
        }

        private string GetStringValue(Dictionary<string, object> row, string key, string defaultValue = "")
        {
            return row.ContainsKey(key) && row[key] != null && row[key] != DBNull.Value
                ? row[key].ToString()
                : defaultValue;
        }

        private DateTime? GetDateValue(Dictionary<string, object> row, string key)
        {
            if (row.ContainsKey(key) && row[key] != null && row[key] != DBNull.Value)
            {
                return Convert.ToDateTime(row[key]);
            }
            return null;
        }

        private static object GetPositionTextForStorage(Employee employee)
        {
            var text = employee.PositionText;
            if (string.IsNullOrWhiteSpace(text) && !employee.PositionId.HasValue)
                text = employee.PositionDisplay;

            return string.IsNullOrWhiteSpace(text) ? DBNull.Value : text;
        }

        private static object ToUpperOrDbNull(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? DBNull.Value
                : value.Trim().ToUpperInvariant();
        }

        private async Task<Dictionary<Guid, string>> LoadReferenceMapAsync(
            List<MetadataObject> catalogs,
            string catalogName)
        {
            var catalog = catalogs.FirstOrDefault(c =>
                c.ObjectType == "Catalog" &&
                c.Name.Equals(catalogName, StringComparison.OrdinalIgnoreCase));

            if (catalog == null)
                return new Dictionary<Guid, string>();

            var displayField = new MetadataField
            {
                DisplayPattern = "{Наименование}",
                DisplayFields = "Наименование"
            };

            var rows = await _metadataService.GetCatalogDataAsync(catalog.Id);
            return rows
                .Where(row => TryGetGuid(row.GetValueOrDefault("Id"), out _))
                .ToDictionary(
                    row => Guid.Parse(row["Id"].ToString()!),
                    row => ReferenceDisplayHelper.BuildDisplayValue(row, displayField));
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
