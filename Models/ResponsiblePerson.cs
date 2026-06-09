using System;

namespace BIS.ERP.Models
{
    public class ResponsiblePerson
    {
        public Guid Id { get; set; }
        public string PersonnelNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public Guid? SiteId { get; set; }
        public string SiteName { get; set; } = string.Empty;  // Для отображения
        public string Position { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}