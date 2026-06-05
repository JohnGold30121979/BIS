using System;

namespace BIS.ERP.Models.Configurator
{
    public class DynamicFieldViewModel
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string DbColumnName { get; set; } = string.Empty;
        public string FieldType { get; set; } = "String";
        public int Length { get; set; } = 100;
        public int Precision { get; set; } = 18;
        public int Scale { get; set; } = 2;
        public bool IsRequired { get; set; }
        public bool IsUnique { get; set; }
        public int Order { get; set; }
    }
}