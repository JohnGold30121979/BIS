namespace BIS.ERP.Models
{
    public class Employee
    {
        public Guid Id { get; set; }
        public string PersonnelNumber { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        
        // Ссылка на должность из справочника
        public Guid? PositionId { get; set; }
        
        // Должность (текст) - используется если не выбрана из справочника
        public string PositionText { get; set; } = string.Empty;

        // Отображаемое значение ссылки на должность из справочника
        public string PositionDisplay { get; set; } = string.Empty;
        
        // Отображаемая должность (для совместимости)
        public string Position 
        { 
            get 
            { 
                return !string.IsNullOrEmpty(PositionText) ? PositionText : PositionDisplay; 
            } 
            set 
            { 
                PositionDisplay = value; 
            } 
        }
        
        // Ссылка на подразделение из справочника
        public Guid? DepartmentId { get; set; }
        
        // Отображаемое подразделение (для совместимости)
        public string Department { get; set; } = string.Empty;
        
        public string Gender { get; set; } = string.Empty;
        public string MaritalStatus { get; set; } = string.Empty;
        
        public DateTime? BirthDate { get; set; }
        public DateTime? HireDate { get; set; }
        public DateTime? TerminationDate { get; set; }
        public string Address { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string Status { get; set; } = "Активен";
        public string Phone { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string TaxId { get; set; } = string.Empty;
        public string PassportNumber { get; set; } = string.Empty;
        public string PassportIssuedBy { get; set; } = string.Empty;
        public DateTime? PassportIssueDate { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
