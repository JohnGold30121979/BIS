using System;
using System.Collections.Generic;

namespace BIS.ERP.Models
{
    public class FoxProAnalysisResult
    {
        public string RootPath { get; set; } = string.Empty;
        public DateTime AnalyzedAt { get; set; } = DateTime.Now;
        public FoxProAnalysisSummary Summary { get; set; } = new();
        public List<FoxProExtensionStatistic> ExtensionStatistics { get; set; } = new();
        public List<FoxProFileSummary> Files { get; set; } = new();
        public List<FoxProSymbolDefinition> Definitions { get; set; } = new();
        public List<FoxProCodeReference> References { get; set; } = new();
        public List<FoxProTableUsage> TableUsages { get; set; } = new();
        public List<FoxProDbfTableInfo> DbfTables { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class FoxProAnalysisSummary
    {
        public int TotalFiles { get; set; }
        public int PrgFiles { get; set; }
        public int FormFiles { get; set; }
        public int ReportFiles { get; set; }
        public int DbfLikeFiles { get; set; }
        public int ProcedureDefinitions { get; set; }
        public int FormCalls { get; set; }
        public int ReportCalls { get; set; }
        public int TableUsages { get; set; }
        public int PostingRuleFiles { get; set; }
    }

    public class FoxProExtensionStatistic
    {
        public string Extension { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalBytes { get; set; }
    }

    public class FoxProFileSummary
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime LastWriteTime { get; set; }
    }

    public class FoxProSymbolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string RawText { get; set; } = string.Empty;
    }

    public class FoxProCodeReference
    {
        public string SourceFile { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string ReferenceType { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string RawText { get; set; } = string.Empty;
    }

    public class FoxProTableUsage
    {
        public string SourceFile { get; set; } = string.Empty;
        public int LineNumber { get; set; }
        public string Operation { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string RawText { get; set; } = string.Empty;
    }

    public class FoxProDbfTableInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public int Version { get; set; }
        public int RecordCount { get; set; }
        public int HeaderLength { get; set; }
        public int RecordLength { get; set; }
        public List<FoxProDbfFieldInfo> Fields { get; set; } = new();
        public string Error { get; set; } = string.Empty;
    }

    public class FoxProDbfFieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Length { get; set; }
        public int DecimalCount { get; set; }
        public int Order { get; set; }
    }
}
