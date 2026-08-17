using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 5432;

        [MaxLength(100)]
        public string DatabaseName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Username { get; set; } = "postgres";

        [MaxLength(100)]
        public string Password { get; set; } = string.Empty;

        // Используем DateTime.UtcNow вместо DateTime.Now
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

       // public DateTime? UpdatedAt { get; set; }

        public bool IsActive { get; set; }

        [MaxLength(50)]
        public string? Version { get; set; }

        [MaxLength(20)]
        public string Icon { get; set; } = DefaultIcon;

        public byte[]? LogoImage { get; set; }

        [MaxLength(50)]
        public string? LogoContentType { get; set; }

        [MaxLength(260)]
        public string? LogoFileName { get; set; }

        public const string DefaultIcon = "🏢";

        // Строка подключения
        [NotMapped]
        public string ConnectionString =>
            $"Host={Host};Port={Port};Database={DatabaseName};Username={Username};Password={Password}";

        [NotMapped]
        public string DisplayIcon => string.IsNullOrWhiteSpace(Icon) ? DefaultIcon : Icon;

        [NotMapped]
        public bool HasLogoImage => LogoImage is { Length: > 0 };

        [NotMapped]
        public bool UsesTextIcon => !HasLogoImage;

        public void NormalizeIcon()
        {
            Icon = string.IsNullOrWhiteSpace(Icon) ? DefaultIcon : Icon.Trim();
        }

        [NotMapped]
        public string TypeName => string.IsNullOrEmpty(Type) ? "Универсальная" : Type;

        [NotMapped]
        public string PatchVersionDisplay => string.IsNullOrWhiteSpace(Version)
            ? "Патч: не указан"
            : $"Патч: {Version}";

        // Отображаемое имя (без хэша)
        [NotMapped]
        public string DisplayName => string.IsNullOrEmpty(Description) ? Name : $"{Name}";
    }
}
