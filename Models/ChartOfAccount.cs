using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BIS.ERP.Models
{
    public class ChartOfAccount
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(20)]
        public string Code { get; set; } = string.Empty; // Код счета (101, 121, 201 и т.д.)

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // Наименование счета

        [MaxLength(500)]
        public string Description { get; set; } = string.Empty; // Описание/назначение

        public int Level { get; set; } = 1; // Уровень счета (1, 2, 3)

        public string AccountType { get; set; } = "Active"; // Active, Passive, ActivePassive

        public Guid? ParentId { get; set; } // Для иерархии счетов

        public int Order { get; set; } = 0;

        public bool IsActive { get; set; } = true;
    }   
}