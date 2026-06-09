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
            var employees = new List<Employee>();

            foreach (var row in data)
            {
                employees.Add(new Employee
                {
                    Id = row.ContainsKey("Id") ? Guid.Parse(row["Id"].ToString()) : Guid.NewGuid(),
                    PersonnelNumber = GetStringValue(row, "Табельный номер"),
                    FullName = GetStringValue(row, "ФИО"),
                    Position = GetStringValue(row, "Должность"),
                    Department = GetStringValue(row, "Подразделение"),
                    HireDate = GetDateValue(row, "Дата приема"),
                    TerminationDate = GetDateValue(row, "Дата увольнения"),
                    Status = GetStringValue(row, "Статус", "Активен"),
                    Phone = GetStringValue(row, "Телефон"),
                    Email = GetStringValue(row, "Email"),
                    TaxId = GetStringValue(row, "ИНН"),
                    IsActive = GetStringValue(row, "Статус", "Активен") == "Активен"
                });
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
                ["Код"] = employee.PersonnelNumber,           // ← добавляем
                ["Наименование"] = employee.FullName,         // ← ОБЯЗАТЕЛЬНОЕ поле!
                ["Табельный номер"] = employee.PersonnelNumber,
                ["ФИО"] = employee.FullName,
                ["Должность"] = employee.Position,
                ["Подразделение"] = employee.Department,
                ["Дата приема"] = employee.HireDate,
                ["Дата увольнения"] = employee.TerminationDate,
                ["Статус"] = employee.Status,
                ["Телефон"] = employee.Phone,
                ["Email"] = employee.Email,
                ["ИНН"] = employee.TaxId
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
                ["Код"] = employee.PersonnelNumber,           // ← добавляем
                ["Наименование"] = employee.FullName,         // ← ОБЯЗАТЕЛЬНОЕ поле!
                ["Табельный номер"] = employee.PersonnelNumber,
                ["ФИО"] = employee.FullName,
                ["Должность"] = employee.Position,
                ["Подразделение"] = employee.Department,
                ["Дата приема"] = employee.HireDate,
                ["Дата увольнения"] = employee.TerminationDate,
                ["Статус"] = employee.Status,
                ["Телефон"] = employee.Phone,
                ["Email"] = employee.Email,
                ["ИНН"] = employee.TaxId
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
                e.Department.Contains(searchText)
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
            return row.ContainsKey(key) && row[key] != null ? row[key].ToString() : defaultValue;
        }

        private DateTime? GetDateValue(Dictionary<string, object> row, string key)
        {
            if (row.ContainsKey(key) && row[key] != null && row[key] != DBNull.Value)
            {
                return Convert.ToDateTime(row[key]);
            }
            return null;
        }
    }
}