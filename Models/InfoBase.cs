using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BIS.ERP.Services;

namespace BIS.ERP.Models
{
    public class InfoBase
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Type { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Description { get; set; }

        // Параметры подключения к PostgreSQL
        [MaxLength(100)]
        public string Host { get; set; } = AppSettings.Instance.Host;

        public int Port { get; set; } = AppSettings.Instance.Port;

        [MaxLength(100)]
        public string DatabaseName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Username { get; set; } = AppSettings.Instance.Username;

        [MaxLength(100)]
        public string Password { get; set; } = AppSettings.Instance.Password;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(20)]
        public string? Version { get; set; }

        // Строка подключения
        [NotMapped]
        public string ConnectionString =>
            $"Host={Host};Port={Port};Database={DatabaseName};Username={Username};Password={Password}";

        [NotMapped]
        public string Icon => "📊";

        [NotMapped]
        public string TypeName => string.IsNullOrEmpty(Type) ? "Универсальная" : Type;
    }
}