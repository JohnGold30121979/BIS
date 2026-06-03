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

    public class Bank
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty; // Полное наименование банка

        [MaxLength(100)]
        public string ShortName { get; set; } = string.Empty; // Сокращенное наименование

        [MaxLength(20)]
        public string BIC { get; set; } = string.Empty; // БИК банка

        [MaxLength(50)]
        public string INN { get; set; } = string.Empty; // ИНН

        [MaxLength(50)]
        public string OKPO { get; set; } = string.Empty; // ОКПО

        [MaxLength(500)]
        public string Address { get; set; } = string.Empty; // Юридический адрес

        [MaxLength(100)]
        public string Phone { get; set; } = string.Empty; // Телефон

        [MaxLength(200)]
        public string Website { get; set; } = string.Empty; // Веб-сайт

        [MaxLength(100)]
        public string Email { get; set; } = string.Empty; // E-mail

        [MaxLength(50)]
        public string SwiftCode { get; set; } = string.Empty; // SWIFT код

        [MaxLength(50)]
        public string CorrespondentAccount { get; set; } = string.Empty; // Корреспондентский счет в НБКР

        public bool IsActive { get; set; } = true;

        public int Order { get; set; } = 0;
    }
}