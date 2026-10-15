using System;
using System.Collections.Generic;
using System.Linq;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Приводит макет печатной формы к виду, пригодному для вывода на лист.
    ///
    /// В макетах с крупной шкалой координат (акт сверки — 78000 x 86854, кассовая
    /// книга, реестр) между строк остаются большие вертикальные разрывы: если их
    /// не сжать, содержимое уезжает за границу листа. Сжатие применяется к любому
    /// макету с такой шкалой — по размеру листа, а не по происхождению формы.
    /// </summary>
    internal static class TemplateLayoutNormalizer
    {
        /// <summary>
        /// Возвращает копию макета со сжатыми вертикальными разрывами.
        ///
        /// Исходный макет не изменяется: вызывающий код может сохранить его
        /// в отчёт как есть.
        /// </summary>
        public static PrintFormTemplate PrepareForRendering(PrintFormTemplate template)
        {
            var clone = Clone(template);
            CompactVerticalGaps(clone, null, null);
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

        /// <summary>
        /// Сжатие нужно только макетам с крупной шкалой координат: у обычного листа
        /// (тысячи единиц) разрывы между строками малы и сжимать нечего.
        /// </summary>
        private static bool ShouldCompact(PrintFormTemplate template)
        {
            return template.PageWidth > 10000 || template.PageHeight > 10000;
        }

        private static PrintFormTemplate Clone(PrintFormTemplate template)
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
    }
}
