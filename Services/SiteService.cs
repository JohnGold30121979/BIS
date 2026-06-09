using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BIS.ERP.Data;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class SiteService
    {
        private readonly AppDbContext _context;

        public SiteService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<List<Site>> GetAllSitesAsync()
        {
            return await _context.Sites
                .OrderBy(s => s.Code)
                .ToListAsync();
        }

        public async Task<Site> GetSiteByIdAsync(Guid id)
        {
            return await _context.Sites.FindAsync(id);
        }

        public async Task<Site> GetSiteByCodeAsync(string code)
        {
            return await _context.Sites
                .FirstOrDefaultAsync(s => s.Code == code);
        }

        public async Task<Site> AddSiteAsync(Site site)
        {
            site.Id = Guid.NewGuid();
            site.CreatedAt = DateTime.UtcNow;
            await _context.Sites.AddAsync(site);
            await _context.SaveChangesAsync();
            return site;
        }

        public async Task<Site> UpdateSiteAsync(Site site)
        {
            var existing = await _context.Sites.FindAsync(site.Id);
            if (existing == null)
                throw new Exception($"Участок с ID {site.Id} не найден");

            existing.Code = site.Code;
            existing.Name = site.Name;
            existing.Description = site.Description;
            existing.IsActive = site.IsActive;
            existing.UpdatedAt = DateTime.UtcNow;

            _context.Sites.Update(existing);
            await _context.SaveChangesAsync();
            return existing;
        }

        public async Task<bool> DeleteSiteAsync(Guid id)
        {
            var site = await _context.Sites.FindAsync(id);
            if (site == null) return false;

            _context.Sites.Remove(site);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<List<Site>> SearchSitesAsync(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return await GetAllSitesAsync();

            return await _context.Sites
                .Where(s => s.Code.Contains(searchText) || s.Name.Contains(searchText))
                .OrderBy(s => s.Code)
                .ToListAsync();
        }
    }
}