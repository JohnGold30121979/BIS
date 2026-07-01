using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class SystemPatchRecord
    {
        [Key]
        [MaxLength(120)]
        public string PatchId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Version { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Checksum { get; set; } = string.Empty;

        public DateTime? AppliedAt { get; set; }

        [MaxLength(120)]
        public string AppliedBy { get; set; } = string.Empty;

        [MaxLength(50)]
        public string AppVersion { get; set; } = string.Empty;

        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        public string Error { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class BisPatchManifest
    {
        public string Format { get; set; } = "BIS.Patch";
        public int FormatVersion { get; set; } = 1;
        public string PatchId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string MinAppVersion { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<string> Dependencies { get; set; } = new();
    }

    public class BisPatchInspectionResult
    {
        public BisPatchManifest Manifest { get; set; } = new();
        public string Checksum { get; set; } = string.Empty;
        public List<string> Entries { get; set; } = new();
    }

    public class BisPatchApplyResult
    {
        public BisPatchManifest Manifest { get; set; } = new();
        public string Checksum { get; set; } = string.Empty;
        public int MetadataObjects { get; set; }
        public int Reports { get; set; }
        public int Modules { get; set; }
        public int DataRows { get; set; }
        public bool SchemaApplied { get; set; }
    }

    public class BisPatchModulesPayload
    {
        public List<MetadataModule> Modules { get; set; } = new();
        public List<MetadataModuleItem> ModuleItems { get; set; } = new();
    }

    public class BisPatchTableData
    {
        public string TableName { get; set; } = string.Empty;
        public string Mode { get; set; } = "Upsert";
        public List<Dictionary<string, object?>> Rows { get; set; } = new();
    }
}
