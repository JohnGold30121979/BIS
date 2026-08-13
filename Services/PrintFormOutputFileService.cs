using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public static class PrintFormOutputFileService
    {
        public static async Task<string> SaveAndOpenAsync(byte[] content, string reportName, PrintFormOutputFormat format)
        {
            if (content == null || content.Length == 0)
                throw new InvalidOperationException("Печатная форма не сформирована.");

            var path = BuildOutputPath(reportName, format);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content);

            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });

            return path;
        }

        public static string GetDisplayName(PrintFormOutputFormat format) =>
            format == PrintFormOutputFormat.Excel ? "Excel" : "PDF";

        private static string BuildOutputPath(string reportName, PrintFormOutputFormat format)
        {
            var extension = format == PrintFormOutputFormat.Excel ? ".xlsx" : ".pdf";
            var safeName = MakeSafeFileName(reportName);
            var fileName = $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
            return Path.Combine(Path.GetTempPath(), "BIS.ERP", "PrintForms", fileName);
        }

        private static string MakeSafeFileName(string? name)
        {
            var value = string.IsNullOrWhiteSpace(name) ? "print_form" : name.Trim();
            foreach (var invalidChar in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidChar, '_');

            value = Regex.Replace(value, @"\s+", "_");
            return value.Length <= 80 ? value : value[..80];
        }
    }
}