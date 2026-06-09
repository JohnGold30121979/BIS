using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class SiteService
    {
        private readonly AppDbContext _context;
        private readonly MetadataService _metadataService;
        private Guid? _catalogId;

        public SiteService(AppDbContext context, MetadataService metadataService)
        {
            _context = context;
            _metadataService = metadataService;
        }

        private async Task<Guid> GetCatalogIdAsync()
        {
            if (_catalogId.HasValue) return _catalogId.Value;

            var catalog = await _context.MetadataObjects
                .FirstOrDefaultAsync(m => m.Name == "Участки" && m.ObjectType == "Catalog");

            _catalogId = catalog?.Id ?? Guid.Empty;
            return _catalogId.Value;
        }

        public async Task<List<Site>> GetAllSitesAsync()
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) return new List<Site>();

            var data = await _metadataService.GetCatalogDataAsync(catalogId);
            var sites = new List<Site>();

            foreach (var row in data)
            {
                sites.Add(new Site
                {
                    Id = row.ContainsKey("Id") ? Guid.Parse(row["Id"].ToString()) : Guid.NewGuid(),
                    SiteCode = GetStringValue(row, "Код участка"),
                    SiteName = GetStringValue(row, "Наименование участка"),
                    Description = GetStringValue(row, "Описание"),
                    IsActive = GetStringValue(row, "Активен", "true") == "true"
                });
            }

            return sites.OrderBy(s => s.SiteCode).ToList();
        }

        public async Task<Site> AddSiteAsync(Site site)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) throw new Exception("Справочник участков не найден");

            var data = new Dictionary<string, object>
            {
                ["Код участка"] = site.SiteCode,
                ["Наименование участка"] = site.SiteName,
                ["Описание"] = site.Description,
                ["Активен"] = site.IsActive
            };

            var recordId = await _metadataService.CreateDynamicRecordAsync(catalogId, data);
            site.Id = recordId;
            return site;
        }

        public async Task<Site> UpdateSiteAsync(Site site)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) throw new Exception("Справочник участков не найден");

            var data = new Dictionary<string, object>
            {
                ["Код участка"] = site.SiteCode,
                ["Наименование участка"] = site.SiteName,
                ["Описание"] = site.Description,
                ["Активен"] = site.IsActive
            };

            await _metadataService.UpdateDynamicRecordAsync(catalogId, site.Id, data);
            return site;
        }

        public async Task<bool> DeleteSiteAsync(Guid id)
        {
            var catalogId = await GetCatalogIdAsync();
            if (catalogId == Guid.Empty) return false;

            await _metadataService.DeleteDynamicRecordAsync(catalogId, id);
            return true;
        }

        private string GetStringValue(Dictionary<string, object> row, string key, string defaultValue = "")
        {
            return row.ContainsKey(key) && row[key] != null ? row[key].ToString() : defaultValue;
        }
    }
}