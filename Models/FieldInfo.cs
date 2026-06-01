// Models/FieldInfo.cs
namespace BIS.ERP.Models
{
    public class FieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "String";
        public bool IsRequired { get; set; } = false;
    }
}