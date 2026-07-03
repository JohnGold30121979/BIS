using BIS.ERP.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class FoxProProjectAnalyzer
    {
        private static readonly Regex ProcedureRegex = new(
            @"^\s*(PROCEDURE|FUNCTION)\s+([A-Za-zА-Яа-я_][\wА-Яа-я]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ClassRegex = new(
            @"^\s*DEFINE\s+CLASS\s+([A-Za-zА-Яа-я_][\wА-Яа-я]*)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DoFormRegex = new(
            @"\bDO\s+FORM\s+(.+?)(?:\s+WITH|\s+NAME|\s+LINKED|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ReportFormRegex = new(
            @"\bREPORT\s+FORM\s+(.+?)(?:\s+TO|\s+PREVIEW|\s+NOCONSOLE|\s+OBJECT|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex DoProcedureRegex = new(
            @"^\s*DO\s+([^\s&+]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex UseRegex = new(
            @"^\s*USE\s+(.+?)(?:\s+AGAIN|\s+ALIAS|\s+ORDER|\s+EXCL|\s+SHARED|\s+IN|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex OpenDatabaseRegex = new(
            @"\bOPEN\s+DATABASE\s+(.+?)(?:\s+SHARED|\s+EXCL|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SetClassLibRegex = new(
            @"^\s*SET\s+CLASSLIB\s+TO\s+(.+?)(?:\s+ADDITIVE|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SetProcedureRegex = new(
            @"^\s*SET\s+PROCEDURE\s+TO\s+(.+?)(?:\s+ADDITIVE|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex AppendFromRegex = new(
            @"^\s*APPEND\s+FROM\s+(.+?)(?:\s+TYPE|\s+FIELDS|\s+FOR|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CopyToRegex = new(
            @"^\s*COPY\s+(?:STRU\s+)?TO\s+(.+?)(?:\s+TYPE|\s+FIELDS|\s+FOR|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex TotalToRegex = new(
            @"\bTOTAL\s+ON\s+.+?\s+TO\s+(.+?)(?:\s+FIELD|\s+FOR|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex IndexToRegex = new(
            @"\bINDEX\s+ON\s+.+?\s+TO\s+(.+?)(?:\s+FOR|\s*$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly HashSet<string> DbfLikeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".dbf", ".pjx", ".scx", ".frx", ".mnx"
        };

        private static readonly HashSet<string> CodeExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".prg", ".mpr"
        };

        public Task<FoxProAnalysisResult> AnalyzeAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => Analyze(rootPath, cancellationToken), cancellationToken);
        }

        public async Task SaveJsonAsync(FoxProAnalysisResult result, string outputPath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(result, options);
            await File.WriteAllTextAsync(outputPath, json, new UTF8Encoding(false));
        }

        private FoxProAnalysisResult Analyze(string rootPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
                throw new ArgumentException("Не указана папка FoxPro-проекта.", nameof(rootPath));

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {rootPath}");

            var root = Path.GetFullPath(rootPath);
            var result = new FoxProAnalysisResult
            {
                RootPath = root,
                AnalyzedAt = DateTime.Now
            };

            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = Path.GetRelativePath(root, file.FullName);
                var extension = file.Extension;

                result.Files.Add(new FoxProFileSummary
                {
                    Name = file.Name,
                    RelativePath = relativePath,
                    Extension = string.IsNullOrWhiteSpace(extension) ? "(без расширения)" : extension,
                    Category = GetCategory(extension),
                    SizeBytes = file.Length,
                    LastWriteTime = file.LastWriteTime
                });

                if (CodeExtensions.Contains(extension))
                    AnalyzeCodeFile(file.FullName, relativePath, result);

                if (DbfLikeExtensions.Contains(extension))
                    result.DbfTables.Add(ReadDbfHeader(file.FullName, relativePath, extension));
            }

            result.ExtensionStatistics = result.Files
                .GroupBy(file => file.Extension, StringComparer.OrdinalIgnoreCase)
                .Select(group => new FoxProExtensionStatistic
                {
                    Extension = group.Key,
                    Count = group.Count(),
                    TotalBytes = group.Sum(file => file.SizeBytes)
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Extension)
                .ToList();

            result.Summary = new FoxProAnalysisSummary
            {
                TotalFiles = result.Files.Count,
                PrgFiles = result.Files.Count(file => file.Extension.Equals(".prg", StringComparison.OrdinalIgnoreCase)),
                FormFiles = result.Files.Count(file => file.Extension.Equals(".scx", StringComparison.OrdinalIgnoreCase)),
                ReportFiles = result.Files.Count(file => file.Extension.Equals(".frx", StringComparison.OrdinalIgnoreCase)),
                DbfLikeFiles = result.DbfTables.Count,
                ProcedureDefinitions = result.Definitions.Count,
                FormCalls = result.References.Count(reference => reference.ReferenceType == "Форма"),
                ReportCalls = result.References.Count(reference => reference.ReferenceType == "Отчет"),
                TableUsages = result.TableUsages.Count,
                PostingRuleFiles = result.Files.Count(file =>
                    file.Name.StartsWith("f_prov", StringComparison.OrdinalIgnoreCase) &&
                    file.Extension.Equals(".prg", StringComparison.OrdinalIgnoreCase))
            };

            result.Warnings.AddRange(BuildWarnings(result));
            return result;
        }

        private static void AnalyzeCodeFile(string filePath, string relativePath, FoxProAnalysisResult result)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath, Encoding.GetEncoding(1251));
            }
            catch
            {
                lines = File.ReadAllLines(filePath, Encoding.UTF8);
                result.Warnings.Add($"Файл прочитан как UTF-8 вместо Windows-1251: {relativePath}");
            }

            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("*"))
                    continue;

                var lineNumber = i + 1;

                AddDefinitionIfMatched(ProcedureRegex.Match(line), "Процедура", relativePath, lineNumber, trimmed, result);
                AddDefinitionIfMatched(ClassRegex.Match(line), "Класс", relativePath, lineNumber, trimmed, result);

                AddReferenceIfMatched(DoFormRegex.Match(line), "Форма", relativePath, lineNumber, trimmed, result);
                AddReferenceIfMatched(ReportFormRegex.Match(line), "Отчет", relativePath, lineNumber, trimmed, result);
                AddReferenceIfMatched(OpenDatabaseRegex.Match(line), "База данных", relativePath, lineNumber, trimmed, result);
                AddReferenceIfMatched(SetClassLibRegex.Match(line), "Библиотека классов", relativePath, lineNumber, trimmed, result);
                AddReferenceIfMatched(SetProcedureRegex.Match(line), "Библиотека процедур", relativePath, lineNumber, trimmed, result);

                var doMatch = DoProcedureRegex.Match(line);
                if (doMatch.Success && !IsIgnoredDoTarget(doMatch.Groups[1].Value))
                    AddReference("Процедура", doMatch.Groups[1].Value, relativePath, lineNumber, trimmed, result);

                AddTableUsageIfMatched(UseRegex.Match(line), "USE", relativePath, lineNumber, trimmed, result);
                AddTableUsageIfMatched(AppendFromRegex.Match(line), "APPEND FROM", relativePath, lineNumber, trimmed, result);
                AddTableUsageIfMatched(CopyToRegex.Match(line), "COPY TO", relativePath, lineNumber, trimmed, result);
                AddTableUsageIfMatched(TotalToRegex.Match(line), "TOTAL TO", relativePath, lineNumber, trimmed, result);
                AddTableUsageIfMatched(IndexToRegex.Match(line), "INDEX TO", relativePath, lineNumber, trimmed, result);
            }
        }

        private static void AddDefinitionIfMatched(
            Match match,
            string kind,
            string relativePath,
            int lineNumber,
            string rawText,
            FoxProAnalysisResult result)
        {
            if (!match.Success)
                return;

            var name = match.Groups.Count > 2 ? match.Groups[2].Value : match.Groups[1].Value;
            result.Definitions.Add(new FoxProSymbolDefinition
            {
                Kind = kind,
                Name = CleanTarget(name),
                SourceFile = relativePath,
                LineNumber = lineNumber,
                RawText = rawText
            });
        }

        private static void AddReferenceIfMatched(
            Match match,
            string referenceType,
            string relativePath,
            int lineNumber,
            string rawText,
            FoxProAnalysisResult result)
        {
            if (!match.Success)
                return;

            AddReference(referenceType, match.Groups[1].Value, relativePath, lineNumber, rawText, result);
        }

        private static void AddReference(
            string referenceType,
            string target,
            string relativePath,
            int lineNumber,
            string rawText,
            FoxProAnalysisResult result)
        {
            var cleanTarget = CleanTarget(target);
            if (string.IsNullOrWhiteSpace(cleanTarget))
                return;

            result.References.Add(new FoxProCodeReference
            {
                ReferenceType = referenceType,
                Target = cleanTarget,
                SourceFile = relativePath,
                LineNumber = lineNumber,
                RawText = rawText
            });
        }

        private static void AddTableUsageIfMatched(
            Match match,
            string operation,
            string relativePath,
            int lineNumber,
            string rawText,
            FoxProAnalysisResult result)
        {
            if (!match.Success)
                return;

            var tableName = CleanTarget(match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(tableName))
                return;

            result.TableUsages.Add(new FoxProTableUsage
            {
                Operation = operation,
                TableName = tableName,
                SourceFile = relativePath,
                LineNumber = lineNumber,
                RawText = rawText
            });
        }

        private static FoxProDbfTableInfo ReadDbfHeader(string filePath, string relativePath, string extension)
        {
            var info = new FoxProDbfTableInfo
            {
                FileName = Path.GetFileName(filePath),
                RelativePath = relativePath,
                Extension = extension
            };

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new BinaryReader(stream);

                if (stream.Length < 32)
                {
                    info.Error = "Файл слишком короткий для DBF-заголовка.";
                    return info;
                }

                info.Version = reader.ReadByte();
                stream.Seek(4, SeekOrigin.Begin);
                info.RecordCount = reader.ReadInt32();
                info.HeaderLength = reader.ReadInt16();
                info.RecordLength = reader.ReadInt16();

                stream.Seek(32, SeekOrigin.Begin);
                var order = 1;
                while (stream.Position + 32 <= stream.Length)
                {
                    var first = reader.ReadByte();
                    if (first == 0x0D)
                        break;

                    stream.Seek(-1, SeekOrigin.Current);
                    var descriptor = reader.ReadBytes(32);
                    if (descriptor.Length < 32 || descriptor[0] == 0x0D)
                        break;

                    var rawName = descriptor.Take(11).TakeWhile(value => value != 0).ToArray();
                    var fieldName = Encoding.ASCII.GetString(rawName).Trim();
                    if (string.IsNullOrWhiteSpace(fieldName))
                        break;

                    info.Fields.Add(new FoxProDbfFieldInfo
                    {
                        Order = order++,
                        Name = fieldName,
                        Type = ((char)descriptor[11]).ToString(),
                        Length = descriptor[16],
                        DecimalCount = descriptor[17]
                    });
                }
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }

            return info;
        }

        private static string GetCategory(string extension)
        {
            return extension.ToLowerInvariant() switch
            {
                ".prg" => "Код PRG",
                ".fxp" => "Скомпилированный PRG",
                ".scx" => "Форма",
                ".sct" => "Memo формы",
                ".frx" => "Отчет",
                ".frt" => "Memo отчета",
                ".dbf" => "Таблица DBF",
                ".pjx" or ".pjt" => "Проект FoxPro",
                ".mnx" or ".mnt" or ".mpr" or ".mpx" => "Меню",
                ".xml" or ".xsd" => "Обмен XML",
                ".exe" or ".app" => "Сборка FoxPro",
                ".dll" or ".fll" or ".tlb" => "Библиотека",
                _ => "Прочее"
            };
        }

        private static string CleanTarget(string value)
        {
            var result = value.Trim();
            if (result.StartsWith("(") && result.EndsWith(")") && result.Length > 2)
                result = result[1..^1].Trim();

            result = result.Trim('\'', '"', '[', ']', ';', ',');
            var commentIndex = result.IndexOf("&&", StringComparison.Ordinal);
            if (commentIndex >= 0)
                result = result[..commentIndex].Trim();

            return result;
        }

        private static bool IsIgnoredDoTarget(string target)
        {
            var normalized = target.Trim().Trim('\'', '"').ToUpperInvariant();
            return normalized is "FORM" or "CASE" or "WHILE" or "WITH" or "EVENTS";
        }

        private static IEnumerable<string> BuildWarnings(FoxProAnalysisResult result)
        {
            if (result.Files.Any(file => file.Name.Equals("fin_oper.pjx", StringComparison.OrdinalIgnoreCase)))
                yield return "Найден основной проект fin_oper.pjx. Для глубокой карты проекта желательно читать memo-поля PJT/PJX.";

            if (result.Files.Any(file => file.Name.Equals("xfrx.prg", StringComparison.OrdinalIgnoreCase)))
                yield return "Найден xfrx.prg. Это крупный модуль печати/экспорта отчетов, его лучше анализировать отдельно.";

            if (result.Summary.PostingRuleFiles > 0)
                yield return $"Найдено файлов правил проводок f_prov*.prg: {result.Summary.PostingRuleFiles}. Их стоит переносить первыми.";

            var failedHeaders = result.DbfTables.Count(table => !string.IsNullOrWhiteSpace(table.Error));
            if (failedHeaders > 0)
                yield return $"Есть DBF-подобные файлы с ошибкой чтения заголовка: {failedHeaders}.";
        }
    }
}
