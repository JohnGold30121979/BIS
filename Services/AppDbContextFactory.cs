using BIS.ERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public sealed class AppDbContextFactory : IAppDbContextFactory
    {
        private readonly InfoBaseManager _infoBaseManager;
        public AppDbContextFactory(InfoBaseManager infoBaseManager) => _infoBaseManager = infoBaseManager;

        public Task<AppDbContext> CreateAsync() => _infoBaseManager.GetCurrentDbContextAsync();
    }
}
