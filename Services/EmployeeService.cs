using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class EmployeeService
    {
        private readonly AppDbContext _context;

        public EmployeeService(AppDbContext context)
        {
            _context = context;
        }

        // Получение всех сотрудников
        public async Task<List<Employee>> GetAllEmployeesAsync()
        {
            return await _context.Employees
                .OrderBy(e => e.PersonnelNumber)
                .ToListAsync();
        }

        // Получение активных сотрудников
        public async Task<List<Employee>> GetActiveEmployeesAsync()
        {
            return await _context.Employees
                .Where(e => e.IsActive && e.Status == "Активен")
                .OrderBy(e => e.PersonnelNumber)
                .ToListAsync();
        }

        // Получение сотрудника по ID
        public async Task<Employee> GetEmployeeByIdAsync(Guid id)
        {
            return await _context.Employees.FindAsync(id);
        }

        // Получение сотрудника по табельному номеру
        public async Task<Employee> GetEmployeeByPersonnelNumberAsync(string personnelNumber)
        {
            return await _context.Employees
                .FirstOrDefaultAsync(e => e.PersonnelNumber == personnelNumber);
        }

        // Добавление сотрудника
        // Добавление сотрудника
        public async Task<Employee> AddEmployeeAsync(Employee employee)
        {
            employee.Id = Guid.NewGuid();
            employee.CreatedAt = DateTime.UtcNow;
            employee.IsActive = employee.Status == "Активен";

            // Гарантируем, что даты в UTC
            if (employee.HireDate.HasValue)
                employee.HireDate = employee.HireDate.Value.ToUniversalTime();
            if (employee.TerminationDate.HasValue)
                employee.TerminationDate = employee.TerminationDate.Value.ToUniversalTime();

            await _context.Employees.AddAsync(employee);
            await _context.SaveChangesAsync();
            return employee;
        }

        // Обновление сотрудника
        public async Task<Employee> UpdateEmployeeAsync(Employee employee)
        {
            var existing = await _context.Employees.FindAsync(employee.Id);
            if (existing == null)
                throw new Exception($"Сотрудник с ID {employee.Id} не найден");

            existing.PersonnelNumber = employee.PersonnelNumber;
            existing.FullName = employee.FullName;
            existing.Position = employee.Position;
            existing.Department = employee.Department;

            // Преобразуем даты в UTC
            existing.HireDate = employee.HireDate?.ToUniversalTime();
            existing.TerminationDate = employee.TerminationDate?.ToUniversalTime();

            existing.Status = employee.Status;
            existing.Phone = employee.Phone;
            existing.Email = employee.Email;
            existing.TaxId = employee.TaxId;
            existing.IsActive = employee.Status == "Активен";
            existing.UpdatedAt = DateTime.UtcNow;

            _context.Employees.Update(existing);
            await _context.SaveChangesAsync();
            return existing;
        }

        // Удаление сотрудника
        public async Task<bool> DeleteEmployeeAsync(Guid id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
                return false;

            _context.Employees.Remove(employee);
            await _context.SaveChangesAsync();
            return true;
        }

        // Увольнение сотрудника
        public async Task<Employee> TerminateEmployeeAsync(Guid id, DateTime terminationDate, string reason = "Уволен")
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
                throw new Exception($"Сотрудник с ID {id} не найден");

            employee.TerminationDate = terminationDate;
            employee.Status = reason;
            employee.IsActive = false;
            employee.UpdatedAt = DateTime.UtcNow;

            _context.Employees.Update(employee);
            await _context.SaveChangesAsync();
            return employee;
        }

        // Импорт сотрудников из JSON
        public async Task<int> ImportEmployeesFromJsonAsync(string jsonContent)
        {
            var employees = System.Text.Json.JsonSerializer.Deserialize<List<Employee>>(jsonContent);
            if (employees == null || !employees.Any())
                return 0;

            int added = 0;
            foreach (var emp in employees)
            {
                var existing = await GetEmployeeByPersonnelNumberAsync(emp.PersonnelNumber);
                if (existing == null)
                {
                    await AddEmployeeAsync(emp);
                    added++;
                }
            }
            return added;
        }

        // Экспорт сотрудников в JSON
        public async Task<string> ExportEmployeesToJsonAsync()
        {
            var employees = await GetAllEmployeesAsync();
            return System.Text.Json.JsonSerializer.Serialize(employees, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        // Поиск сотрудников
        public async Task<List<Employee>> SearchEmployeesAsync(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return await GetAllEmployeesAsync();

            return await _context.Employees
                .Where(e => e.PersonnelNumber.Contains(searchText) ||
                            e.FullName.Contains(searchText) ||
                            e.Position.Contains(searchText) ||
                            e.Department.Contains(searchText))
                .OrderBy(e => e.PersonnelNumber)
                .ToListAsync();
        }
    }
}