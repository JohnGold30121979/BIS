using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BIS.ERP.Models
{
    public class AppUpdateRecord
    {
        [Key]
        [MaxLength(120)]
        public string UpdateId { get; set; } = string.Empty;

        [MaxLength(50)]
        public string Version { get; set; } = string.Empty;

        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [MaxLength(64)]
        public string Checksum { get; set; } = string.Empty;

        public DateTime? AppliedAt { get; set; }

        [MaxLength(120)]
        public string AppliedBy { get; set; } = string.Empty;

        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        public string Error { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class AppUpdateManifest
    {
        public string Format { get; set; } = "BIS.AppUpdate";
        public int FormatVersion { get; set; } = 1;
        public string UpdateId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string MinAppVersion { get; set; } = string.Empty;
        public string RestartExecutable { get; set; } = "BIS.ERP.exe";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<AppUpdateFile> Files { get; set; } = new();
    }

    public class AppUpdateFile
    {
        public string RelativePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    public class AppUpdateInspectionResult
    {
        public AppUpdateManifest Manifest { get; set; } = new();
        public string Checksum { get; set; } = string.Empty;
        public List<string> Entries { get; set; } = new();
    }

    public class AppUpdateStageResult
    {
        public AppUpdateManifest Manifest { get; set; } = new();
        public string Checksum { get; set; } = string.Empty;
        public string StagingDirectory { get; set; } = string.Empty;
        public string PlanFilePath { get; set; } = string.Empty;
        public string UpdaterExecutablePath { get; set; } = string.Empty;
    }

    public class AppUpdateFeedManifest
    {
        public string Format { get; set; } = "BIS.AppUpdateFeed";
        public int FormatVersion { get; set; } = 1;
        public string UpdateId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string MinAppVersion { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; } = DateTime.UtcNow;
    }

    public class AppUpdateOnlineCheckResult
    {
        public string SourceUrl { get; set; } = string.Empty;
        public string PackageUrl { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string UpdateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public string LocalPackagePath { get; set; } = string.Empty;
        public bool IsUpdateAvailable { get; set; }
        public bool IsDirectPackage { get; set; }
    }

    public class AppUpdatePlan
    {
        public string UpdateId { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public int MainProcessId { get; set; }
        public string SourceDirectory { get; set; } = string.Empty;
        public string TargetDirectory { get; set; } = string.Empty;
        public string BackupDirectory { get; set; } = string.Empty;
        public string RestartExecutable { get; set; } = string.Empty;
        public string RestartArguments { get; set; } = string.Empty;
        public string HistoryFilePath { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
