public class DbfFieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int Length { get; set; }
    public int DecimalCount { get; set; }
    public List<string> SampleValues { get; set; } = new List<string>(); // Добавьте это свойство
}