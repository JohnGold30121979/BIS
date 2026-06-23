using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class MetadataModule
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        [MaxLength(80)] public string Code { get; set; } = string.Empty;
        [MaxLength(160)] public string Name { get; set; } = string.Empty;
        [MaxLength(600)] public string Description { get; set; } = string.Empty;
        [MaxLength(20)] public string Icon { get; set; } = "📁";
        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsSystem { get; set; }
    }

    public class MetadataModuleItem
    {
        [Key] public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ModuleId { get; set; }
        public Guid ObjectId { get; set; }
        [MaxLength(30)] public string ObjectType { get; set; } = string.Empty;
        public int Order { get; set; }
    }
}
