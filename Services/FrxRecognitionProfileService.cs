using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    internal static class FrxRecognitionProfileService
    {
        private const string ProfilesRelativePath = "ReportRecognition/frx-recognition-profiles.json";
        private static readonly Lazy<IReadOnlyList<FrxRecognitionProfile>> Profiles = new(LoadProfiles);

        public static PrintFormTemplate ApplyImportProfile(PrintFormTemplate template)
        {
            var profile = DetectProfile(template);
            if (profile == null)
                return template;

            template.RecognitionProfileCode = profile.Code;
            ApplyAlignmentOverrides(template, profile);
            if (profile.Layout.NormalizeBandTops)
                NormalizeBandTops(template);
            if (profile.Layout.CompactVerticalGaps && !template.LayoutNormalized)
            {
                CompactVerticalGaps(template, profile.Layout.DesiredVerticalGap, profile.Layout.MinGapToCompact);
                template.LayoutNormalized = true;
            }

            return template;
        }

        public static PrintFormTemplate PrepareForRendering(PrintFormTemplate template)
        {
            var profile = DetectProfile(template);
            if (profile == null)
                return CompactVerticalGapsClone(template, null, null);

            if (string.IsNullOrWhiteSpace(template.RecognitionProfileCode))
                template.RecognitionProfileCode = profile.Code;

            if (profile.Layout.CompactVerticalGaps && template.LayoutNormalized)
                return template;

            return profile.Layout.CompactVerticalGaps
                ? CompactVerticalGapsClone(template, profile.Layout.DesiredVerticalGap, profile.Layout.MinGapToCompact)
                : template;
        }

        public static IReadOnlyDictionary<string, string> GetDefaultVariables(string? profileCode = null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ko1"] = "1",
                ["LL"] = "70",
                ["sha"] = string.Empty,
                ["sha1"] = string.Empty,
                ["sha2"] = string.Empty
            };

            var profiles = string.IsNullOrWhiteSpace(profileCode)
                ? Profiles.Value
                : Profiles.Value.Where(profile => profile.Code.Equals(profileCode, StringComparison.OrdinalIgnoreCase));

            foreach (var profile in profiles)
            foreach (var pair in profile.DefaultVariables)
                result[pair.Key] = pair.Value;

            return result;
        }

        private static FrxRecognitionProfile? DetectProfile(PrintFormTemplate template)
        {
            if (!string.IsNullOrWhiteSpace(template.RecognitionProfileCode))
            {
                var direct = Profiles.Value.FirstOrDefault(profile =>
                    profile.Code.Equals(template.RecognitionProfileCode, StringComparison.OrdinalIgnoreCase));
                if (direct != null)
                    return direct;
            }

            var fileName = Path.GetFileName(template.OriginalFileName);
            foreach (var profile in Profiles.Value)
            {
                if (!string.IsNullOrWhiteSpace(fileName) &&
                    profile.FileNamePatterns.Any(pattern => IsWildcardMatch(fileName, pattern)))
                    return profile;
            }

            var text = string.Join("\n", template.Elements.Select(element =>
                string.IsNullOrWhiteSpace(element.Expression) ? element.Text : element.Expression));
            return Profiles.Value.FirstOrDefault(profile =>
                profile.RequiredExpressions.Count > 0 &&
                profile.RequiredExpressions.All(expression =>
                    text.Contains(expression, StringComparison.OrdinalIgnoreCase)));
        }

        private static bool IsWildcardMatch(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
                return false;

            var regex = "^" + Regex.Escape(pattern.Trim())
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
        }

        private static void ApplyAlignmentOverrides(PrintFormTemplate template, FrxRecognitionProfile profile)
        {
            foreach (var element in template.Elements)
            {
                var text = string.IsNullOrWhiteSpace(element.Expression) ? element.Text : element.Expression;
                foreach (var rule in profile.AlignmentOverrides)
                {
                    if (Regex.IsMatch(text.Trim(), rule.ExpressionPattern, RegexOptions.IgnoreCase))
                    {
                        element.Alignment = rule.Alignment;
                        break;
                    }
                }
            }
        }

        private static void NormalizeBandTops(PrintFormTemplate template)
        {
            if (template.Bands.Count == 0)
                return;

            var hasMeaningfulTop = template.Bands.Any(band => band.Top > 0);
            if (hasMeaningfulTop)
                return;

            double top = 0;
            foreach (var band in template.Bands.OrderBy(band => band.Order))
            {
                band.Top = top;
                top += Math.Max(1, band.Height);
            }
        }

        private static PrintFormTemplate CompactVerticalGapsClone(
            PrintFormTemplate template,
            double? desiredVerticalGap,
            double? minGapToCompact)
        {
            var clone = CloneTemplate(template);
            CompactVerticalGaps(clone, desiredVerticalGap, minGapToCompact);
            return clone;
        }

        private static void CompactVerticalGaps(
            PrintFormTemplate template,
            double? desiredVerticalGap,
            double? minGapToCompact)
        {
            if (!ShouldCompact(template) || template.Elements.Count < 2)
                return;

            var orderedElements = template.Elements
                .Where(element => element.Height >= 0)
                .OrderBy(element => element.Top)
                .ThenBy(element => element.Left)
                .ToList();
            if (orderedElements.Count < 2)
                return;

            var desiredGap = desiredVerticalGap ?? Math.Clamp(template.PageHeight * 0.012, 500, 900);
            var compactThreshold = minGapToCompact ?? desiredGap * 2.2;
            var currentBottom = orderedElements[0].Top + orderedElements[0].Height;
            var shifts = new List<(double ThresholdTop, double Shift)>();

            foreach (var element in orderedElements.Skip(1))
            {
                if (element.Top > currentBottom)
                {
                    var gap = element.Top - currentBottom;
                    if (gap > compactThreshold)
                        shifts.Add((element.Top, gap - desiredGap));
                }

                currentBottom = Math.Max(currentBottom, element.Top + element.Height);
            }

            if (shifts.Count == 0)
                return;

            foreach (var element in template.Elements)
            {
                var shift = shifts
                    .Where(item => element.Top >= item.ThresholdTop)
                    .Sum(item => item.Shift);
                element.Top -= shift;
            }

            foreach (var band in template.Bands)
            {
                var shift = shifts
                    .Where(item => band.Top >= item.ThresholdTop)
                    .Sum(item => item.Shift);
                band.Top -= shift;
            }

            var contentBottom = template.Elements.Max(element => element.Top + element.Height);
            template.PageHeight = Math.Max(1000, contentBottom + desiredGap);
        }

        private static bool ShouldCompact(PrintFormTemplate template)
        {
            return template.SourceFormat.Equals("FoxProFRX", StringComparison.OrdinalIgnoreCase) ||
                   template.OriginalFileName.EndsWith(".frx", StringComparison.OrdinalIgnoreCase) ||
                   template.PageWidth > 10000 ||
                   template.PageHeight > 10000;
        }

        private static PrintFormTemplate CloneTemplate(PrintFormTemplate template)
        {
            return new PrintFormTemplate
            {
                SourceFormat = template.SourceFormat,
                OriginalFileName = template.OriginalFileName,
                RecognitionProfileCode = template.RecognitionProfileCode,
                LayoutNormalized = template.LayoutNormalized,
                PageWidth = template.PageWidth,
                PageHeight = template.PageHeight,
                Bands = template.Bands.Select(band => new PrintFormBand
                {
                    Type = band.Type,
                    Top = band.Top,
                    Height = band.Height,
                    Order = band.Order
                }).ToList(),
                Elements = template.Elements.Select(element => new PrintFormElement
                {
                    Type = element.Type,
                    Text = element.Text,
                    Expression = element.Expression,
                    BandType = element.BandType,
                    Left = element.Left,
                    Top = element.Top,
                    Width = element.Width,
                    Height = element.Height,
                    FontName = element.FontName,
                    FontSize = element.FontSize,
                    Bold = element.Bold,
                    Italic = element.Italic,
                    Alignment = element.Alignment,
                    BorderStyle = element.BorderStyle,
                    Order = element.Order
                }).ToList()
            };
        }

        private static IReadOnlyList<FrxRecognitionProfile> LoadProfiles()
        {
            try
            {
                var file = Path.Combine(AppContext.BaseDirectory, ProfilesRelativePath);
                if (File.Exists(file))
                {
                    var container = JsonSerializer.Deserialize<FrxRecognitionProfileContainer>(
                        File.ReadAllText(file),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (container?.Profiles.Count > 0)
                        return container.Profiles;
                }
            }
            catch
            {
                // При поврежденной базе правил используем безопасные встроенные профили.
            }

            return GetFallbackProfiles();
        }

        private static IReadOnlyList<FrxRecognitionProfile> GetFallbackProfiles() => new[]
        {
            new FrxRecognitionProfile
            {
                Code = "foxpro_cash_receipt_order",
                Name = "Visual FoxPro - Приходный кассовый ордер",
                FileNamePatterns = new List<string> { "pr_pri4.frx" },
                RequiredExpressions = new List<string> { "iif(ko1=1,sum,sum_v)", "Msum1", "DOK1" },
                DefaultVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ko1"] = "1",
                    ["LL"] = "70",
                    ["sha"] = string.Empty,
                    ["sha1"] = string.Empty,
                    ["sha2"] = string.Empty
                },
                Layout = new FrxLayoutRecognitionOptions
                {
                    NormalizeBandTops = true,
                    CompactVerticalGaps = true,
                    DesiredVerticalGap = 700,
                    MinGapToCompact = 1500
                },
                AlignmentOverrides = new List<FrxAlignmentOverride>
                {
                    new() { ExpressionPattern = "^(DEB|CRED|iif\\(ko1=1,sum,sum_v\\))$", Alignment = "Center" },
                    new() { ExpressionPattern = "^alltrim\\(DOK1\\)$", Alignment = "Center" }
                }
            },
            new FrxRecognitionProfile
            {
                Code = "foxpro_invoice_kg",
                Name = "Visual FoxPro - Счет-фактура КР",
                FileNamePatterns = new List<string> { "pr_fak16.frx" },
                RequiredExpressions = new List<string> { "fact.C_INN", "fact.D_INN", "curFACTSW" },
                DefaultVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sha"] = string.Empty,
                    ["sha1"] = string.Empty,
                    ["sha2"] = string.Empty
                },
                Layout = new FrxLayoutRecognitionOptions
                {
                    NormalizeBandTops = true,
                    CompactVerticalGaps = false,
                    DesiredVerticalGap = 700,
                    MinGapToCompact = 1500
                }
            }
        };

        private sealed class FrxRecognitionProfileContainer
        {
            public List<FrxRecognitionProfile> Profiles { get; set; } = new();
        }

        private sealed class FrxRecognitionProfile
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public List<string> FileNamePatterns { get; set; } = new();
            public List<string> RequiredExpressions { get; set; } = new();
            public Dictionary<string, string> DefaultVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
            public FrxLayoutRecognitionOptions Layout { get; set; } = new();
            public List<FrxAlignmentOverride> AlignmentOverrides { get; set; } = new();
        }

        private sealed class FrxLayoutRecognitionOptions
        {
            public bool NormalizeBandTops { get; set; }
            public bool CompactVerticalGaps { get; set; }
            public double DesiredVerticalGap { get; set; } = 700;
            public double MinGapToCompact { get; set; } = 1500;
        }

        private sealed class FrxAlignmentOverride
        {
            public string ExpressionPattern { get; set; } = string.Empty;
            public string Alignment { get; set; } = "Left";
        }
    }
}
