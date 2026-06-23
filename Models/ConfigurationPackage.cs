using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class ConfigurationPackage
    {
        public string Format { get; set; } = "BIS.Configuration";
        public int Version { get; set; } = 1;
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public string Application { get; set; } = "BIS ERP";
        public List<SystemConfiguration> SystemConfigurations { get; set; } = new();
        public List<MetadataObject> MetadataObjects { get; set; } = new();
        public List<Report> Reports { get; set; } = new();
        public List<MetadataModule> Modules { get; set; } = new();
        public List<MetadataModuleItem> ModuleItems { get; set; } = new();
        public List<ConfigurationTableData> TableData { get; set; } = new();
    }

    public class ConfigurationTableData
    {
        public Guid MetadataObjectId { get; set; }
        public string ObjectName { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
    }
}
