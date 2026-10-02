using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public partial class MetadataService
    {
        /// <summary>
        /// Строки затрат авансового платежа из отдельной таблицы строк
        /// (классическая модель, как у счет-фактур). Обёртка над AdvancePaymentService.
        /// </summary>
        public Task<List<AdvancePaymentLine>> GetAdvancePaymentLinesAsync(string headerTableName, Guid documentId)
        {
            var service = new AdvancePaymentService(_context);
            service.Configure(headerTableName);
            return service.GetLinesAsync(documentId);
        }

        public Task<Dictionary<Guid, List<AdvancePaymentLine>>> GetAdvancePaymentLinesForDocumentsAsync(
            string headerTableName,
            IReadOnlyCollection<Guid> documentIds)
        {
            var service = new AdvancePaymentService(_context);
            service.Configure(headerTableName);
            return service.GetLinesForDocumentsAsync(documentIds);
        }

        public Task SaveAdvancePaymentLinesAsync(
            string headerTableName,
            Guid documentId,
            IReadOnlyList<AdvancePaymentLine> lines)
        {
            var service = new AdvancePaymentService(_context);
            service.Configure(headerTableName);
            return service.SaveLinesAsync(documentId, lines);
        }
    }
}