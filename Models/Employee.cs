namespace BIS.ERP.Models
{
    public class Employee
    {
        public Guid Id { get; set; }
        public string PersonnelNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Position { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public string Status { get; set; } = "Активен";
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string TaxId { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}