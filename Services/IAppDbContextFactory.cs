using BIS.ERP.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public interface IAppDbContextFactory
    {
        /// <summary>Контекст текущей информационной базы. Вызывающий обязан освободить.</summary>
        Task<AppDbContext> CreateAsync();
    }
}
