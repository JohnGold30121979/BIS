using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class ResponsiblePersonService
    {
        private readonly AppDbContext _context;

        public ResponsiblePersonService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<ResponsiblePerson>> GetAllResponsiblePersonsAsync()
        {
            return await _context.ResponsiblePersons
                .Include(r => r.Site)
                .OrderBy(r => r.PersonnelNumber)
                .ToListAsync();
        }

        public async Task<ResponsiblePerson> GetResponsiblePersonByIdAsync(Guid id)
        {
            return await _context.ResponsiblePersons
                .Include(r => r.Site)
                .FirstOrDefaultAsync(r => r.Id == id);
        }

        public async Task<ResponsiblePerson> AddResponsiblePersonAsync(ResponsiblePerson person)
        {
            person.Id = Guid.NewGuid();
            person.CreatedAt = DateTime.UtcNow;
            await _context.ResponsiblePersons.AddAsync(person);
            await _context.SaveChangesAsync();
            return person;
        }

        public async Task<ResponsiblePerson> UpdateResponsiblePersonAsync(ResponsiblePerson person)
        {
            var existing = await _context.ResponsiblePersons.FindAsync(person.Id);
            if (existing == null)
                throw new Exception($"МОЛ с ID {person.Id} не найден");

            existing.PersonnelNumber = person.PersonnelNumber;
            existing.FullName = person.FullName;
            existing.SiteId = person.SiteId;
            existing.Note = person.Note;
            existing.IsActive = person.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            _context.ResponsiblePersons.Update(existing);
            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteResponsiblePersonAsync(Guid id)
        {
            var person = await _context.ResponsiblePersons.FindAsync(id);
            if (person == null) return false;

            _context.ResponsiblePersons.Remove(person);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<ResponsiblePerson>> SearchResponsiblePersonsAsync(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return await GetAllResponsiblePersonsAsync();

            return await _context.ResponsiblePersons
                .Include(r => r.Site)
                .Where(r => r.PersonnelNumber.Contains(searchText) || r.FullName.Contains(searchText))
                .OrderBy(r => r.PersonnelNumber)
                .ToListAsync();
        }

        public async Task<List<ResponsiblePerson>> GetBySiteAsync(Guid siteId)
        {
            return await _context.ResponsiblePersons
                .Include(r => r.Site)
                .Where(r => r.SiteId == siteId && r.IsActive)
                .OrderBy(r => r.PersonnelNumber)
                .ToListAsync();
        }
    }
}