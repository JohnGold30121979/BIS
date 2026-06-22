using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BIS.ERP.Models;
using System.Text.Json;
using System.Buffers.Binary;
using System.Globalization;

namespace BIS.ERP.Services
{
    public class FrxParser
    {
        public FrxReport ParseFrxFile(string frxPath)
        {
            if (string.IsNullOrWhiteSpace(frxPath) || !File.Exists(frxPath))
                throw new FileNotFoundException("FRX файл не найден", frxPath);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var report = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(frxPath),
                OriginalFileName = Path.GetFileName(frxPath),
                FrxData = File.ReadAllBytes(frxPath),
                CreatedAt = DateTime.UtcNow
            };
            var records = ReadVisualFoxProRecords(frxPath);
            var template = BuildVisualFoxProTemplate(report, records);
            report.FrxXml = JsonSerializer.Serialize(template);
            report.ReportType = "FoxProLayout";
            report.Description = $"Импортирован из Visual FoxPro: {report.OriginalFileName}";
            return report;
        }

        public PrintFormTemplate GetPrintTemplate(FrxReport report)
        {
            return string.IsNullOrWhiteSpace(report.FrxXml)
                ? new PrintFormTemplate { OriginalFileName = report.OriginalFileName }
                : JsonSerializer.Deserialize<PrintFormTemplate>(report.FrxXml) ?? new PrintFormTemplate();
        }

        public FrxReport ParseFrxFile(byte[] frxData, string fileName)
        {
            var report = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(fileName),
                OriginalFileName = fileName,
                FrxData = frxData,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                // Пытаемся прочитать как XML (более новые версии FRX)
                var xmlContent = Encoding.UTF8.GetString(frxData);
                if (xmlContent.TrimStart().StartsWith("<"))
                {
                    ParseFrxXml(report, xmlContent);
                }
                else
                {
                    // Старый бинарный формат FoxPro
                    ParseFrxBinary(report, frxData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FRX Parse Error: {ex.Message}");
                // Создаем структуру по умолчанию
                CreateDefaultStructure(report);
            }

            return report;
        }

        private void ParseFrxXml(FrxReport report, string xmlContent)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlContent);

            // Парсим банды
            var bandsNode = doc.SelectSingleNode("//Bands");
            if (bandsNode != null)
            {
                int order = 0;
                foreach (XmlNode bandNode in bandsNode.ChildNodes)
                {
                    var band = new FrxBand
                    {
                        Id = Guid.NewGuid(),
                        ReportId = report.Id,
                        BandType = bandNode.Name,
                        Height = GetIntAttribute(bandNode, "Height", 50),
                        Top = GetIntAttribute(bandNode, "Top", 0),
                        Order = order++,
                        IsVisible = GetBoolAttribute(bandNode, "Visible", true)
                    };

                    // Парсим поля в банде
                    var fieldsNode = bandNode.SelectSingleNode("Fields");
                    if (fieldsNode != null)
                    {
                        int fieldOrder = 0;
                        foreach (XmlNode fieldNode in fieldsNode.ChildNodes)
                        {
                            var field = new FrxField
                            {
                                Id = Guid.NewGuid(),
                                BandId = band.Id,
                                FieldName = GetStringAttribute(fieldNode, "Name"),
                                FieldType = GetStringAttribute(fieldNode, "Type", "Text"),
                                Left = GetIntAttribute(fieldNode, "Left", 0),
                                Top = GetIntAttribute(fieldNode, "Top", 0),
                                Width = GetIntAttribute(fieldNode, "Width", 100),
                                Height = GetIntAttribute(fieldNode, "Height", 20),
                                Format = GetStringAttribute(fieldNode, "Format"),
                                Alignment = GetStringAttribute(fieldNode, "Alignment", "Left"),
                                FontName = GetStringAttribute(fieldNode, "FontName", "Segoe UI"),
                                FontSize = GetIntAttribute(fieldNode, "FontSize", 10),
                                FontBold = GetBoolAttribute(fieldNode, "FontBold"),
                                Order = fieldOrder++,
                                IsVisible = GetBoolAttribute(fieldNode, "Visible", true)
                            };
                            report.Fields.Add(field);
                        }
                    }

                    report.Bands.Add(band);
                }
            }

            // Парсим выражения
            var expressionsNode = doc.SelectSingleNode("//Expressions");
            if (expressionsNode != null)
            {
                int order = 0;
                foreach (XmlNode exprNode in expressionsNode.ChildNodes)
                {
                    var expr = new FrxExpression
                    {
                        Id = Guid.NewGuid(),
                        ReportId = report.Id,
                        Name = GetStringAttribute(exprNode, "Name"),
                        Expression = exprNode.InnerText,
                        Order = order++
                    };
                    report.Expressions.Add(expr);
                }
            }
        }

        private void ParseFrxBinary(FrxReport report, byte[] data)
        {
            // Бинарный парсер для старых FRX файлов
            // Формат: записи фиксированной длины

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            // Пропускаем заголовок
            reader.BaseStream.Seek(10, SeekOrigin.Begin);

            // Читаем записи
            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                try
                {
                    var recordType = reader.ReadByte(); // 0x42 = Band, 0x46 = Field, 0x45 = Expression
                    var recordLength = reader.ReadInt16();

                    if (recordType == 0x42) // Band
                    {
                        var band = ParseBinaryBand(reader);
                        band.ReportId = report.Id;
                        report.Bands.Add(band);
                    }
                    else if (recordType == 0x46) // Field
                    {
                        var field = ParseBinaryField(reader);
                        // Привязываем к последней банде
                        if (report.Bands.Any())
                        {
                            field.BandId = report.Bands.Last().Id;
                            report.Fields.Add(field);
                        }
                    }
                    else
                    {
                        // Пропускаем неизвестную запись
                        reader.BaseStream.Seek(recordLength - 3, SeekOrigin.Current);
                    }
                }
                catch (EndOfStreamException)
                {
                    break;
                }
            }

            // Если ничего не нашли, создаем структуру по умолчанию
            if (!report.Bands.Any())
            {
                CreateDefaultStructure(report);
            }
        }

        private static List<Dictionary<string, object?>> ReadVisualFoxProRecords(string frxPath)
        {
            var memoPath = Path.ChangeExtension(frxPath, ".FRT");
            var dbf = File.ReadAllBytes(frxPath);
            var memo = File.Exists(memoPath) ? File.ReadAllBytes(memoPath) : Array.Empty<byte>();
            var encoding = Encoding.GetEncoding(1251);
            var recordCount = BinaryPrimitives.ReadInt32LittleEndian(dbf.AsSpan(4, 4));
            var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf.AsSpan(8, 2));
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf.AsSpan(10, 2));
            var fields = new List<VfpField>();
            for (var offset = 32; offset + 32 <= headerLength && dbf[offset] != 0x0D; offset += 32)
            {
                var descriptor = dbf.AsSpan(offset, 32);
                var nameLength = descriptor[..11].IndexOf((byte)0);
                if (nameLength < 0) nameLength = 11;
                fields.Add(new VfpField(
                    Encoding.ASCII.GetString(descriptor[..nameLength]),
                    (char)descriptor[11], descriptor[16], descriptor[17]));
            }
            var memoBlockSize = memo.Length >= 8
                ? BinaryPrimitives.ReadUInt16BigEndian(memo.AsSpan(6, 2))
                : 0;
            var records = new List<Dictionary<string, object?>>();
            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                var recordOffset = headerLength + recordIndex * recordLength;
                if (recordOffset + recordLength > dbf.Length || dbf[recordOffset] == (byte)'*')
                    continue;
                var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var fieldOffset = recordOffset + 1;
                foreach (var field in fields)
                {
                    var raw = dbf.AsSpan(fieldOffset, field.Length);
                    fieldOffset += field.Length;
                    record[field.Name] = field.Type switch
                    {
                        'M' => ReadMemo(raw, memo, memoBlockSize, encoding),
                        'N' or 'F' => ReadNumber(raw, encoding),
                        'L' => raw.Length > 0 && raw[0] is (byte)'T' or (byte)'t' or (byte)'Y' or (byte)'y',
                        _ => encoding.GetString(raw).Trim('\0', ' ')
                    };
                }
                records.Add(record);
            }
            return records;
        }

        private static string ReadMemo(ReadOnlySpan<byte> pointerBytes, byte[] memo, int blockSize, Encoding encoding)
        {
            if (pointerBytes.Length < 4 || memo.Length == 0 || blockSize <= 0)
                return string.Empty;
            var block = BinaryPrimitives.ReadInt32LittleEndian(pointerBytes[..4]);
            var offset = block * blockSize;
            if (block <= 0 || offset < 0 || offset + 8 > memo.Length)
                return string.Empty;
            var length = BinaryPrimitives.ReadInt32BigEndian(memo.AsSpan(offset + 4, 4));
            if (length <= 0 || offset + 8 + length > memo.Length)
                return string.Empty;
            return encoding.GetString(memo, offset + 8, length).Trim('\0', ' ');
        }

        private static object? ReadNumber(ReadOnlySpan<byte> raw, Encoding encoding)
        {
            var text = encoding.GetString(raw).Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number
                : text;
        }

        private static PrintFormTemplate BuildVisualFoxProTemplate(
            FrxReport report,
            IReadOnlyCollection<Dictionary<string, object?>> records)
        {
            var template = new PrintFormTemplate
            {
                SourceFormat = "FoxProFRX",
                OriginalFileName = report.OriginalFileName
            };
            var reportRecord = records.FirstOrDefault(record => GetInt(record, "OBJTYPE") == 1);
            if (reportRecord != null)
            {
                template.PageWidth = Math.Max(1, GetDouble(reportRecord, "WIDTH"));
                template.PageHeight = Math.Max(1, GetDouble(reportRecord, "HEIGHT"));
            }

            var bandRecords = records.Where(record => GetInt(record, "OBJTYPE") == 9)
                .OrderBy(record => GetDouble(record, "VPOS")).ToList();
            var order = 0;
            foreach (var record in bandRecords)
            {
                var band = new FrxBand
                {
                    Id = Guid.NewGuid(), ReportId = report.Id,
                    BandType = GetVfpBandType(GetInt(record, "OBJCODE")),
                    Top = (int)Math.Round(GetDouble(record, "VPOS")),
                    Height = Math.Max(1, (int)Math.Round(GetDouble(record, "HEIGHT"))),
                    Width = Math.Max(1, (int)Math.Round(GetDouble(record, "WIDTH"))),
                    Order = order++, IsVisible = true
                };
                report.Bands.Add(band);
                template.Bands.Add(new PrintFormBand
                {
                    Type = band.BandType, Top = band.Top, Height = band.Height, Order = band.Order
                });
            }

            var elementRecords = records.Where(record => GetInt(record, "OBJTYPE") is 5 or 6 or 7 or 8 or 17)
                .OrderBy(record => GetDouble(record, "VPOS"))
                .ThenBy(record => GetDouble(record, "HPOS"));
            order = 0;
            foreach (var record in elementRecords)
            {
                var objectType = GetInt(record, "OBJTYPE");
                var top = GetDouble(record, "VPOS");
                var band = FindBand(report.Bands, top);
                var expression = GetText(record, "EXPR");
                var field = new FrxField
                {
                    Id = Guid.NewGuid(), BandId = band?.Id ?? Guid.Empty,
                    FieldName = objectType == 5 ? expression : GetText(record, "NAME"),
                    Expression = objectType == 8 ? expression : string.Empty,
                    FieldType = GetVfpElementType(objectType),
                    Left = (int)Math.Round(GetDouble(record, "HPOS")),
                    Top = (int)Math.Round(top),
                    Width = Math.Max(1, (int)Math.Round(GetDouble(record, "WIDTH"))),
                    Height = Math.Max(1, (int)Math.Round(GetDouble(record, "HEIGHT"))),
                    FontName = GetText(record, "FONTFACE", "Arial"),
                    FontSize = Math.Max(1, (int)Math.Round(GetDouble(record, "FONTSIZE", 9))),
                    FontBold = (GetInt(record, "FONTSTYLE") & 1) == 1,
                    Alignment = GetVfpAlignment(GetInt(record, "OFFSET")),
                    BorderStyle = objectType is 6 or 7 ? "Solid" : "None",
                    IsVisible = true, Order = order++
                };
                report.Fields.Add(field);
                band?.Fields.Add(field);
                template.Elements.Add(new PrintFormElement
                {
                    Type = field.FieldType,
                    Text = objectType == 5 ? expression : string.Empty,
                    Expression = field.Expression,
                    BandType = band?.BandType ?? "Detail",
                    Left = field.Left, Top = field.Top, Width = field.Width, Height = field.Height,
                    FontName = field.FontName, FontSize = field.FontSize,
                    Bold = field.FontBold, Italic = (GetInt(record, "FONTSTYLE") & 2) == 2,
                    Alignment = field.Alignment, BorderStyle = field.BorderStyle, Order = field.Order
                });
            }

            if (template.Elements.Count > 0)
            {
                template.PageWidth = Math.Max(template.PageWidth,
                    template.Elements.Max(element => element.Left + element.Width) + 1000);
                template.PageHeight = Math.Max(template.PageHeight,
                    template.Elements.Max(element => element.Top + element.Height) + 1000);
            }

            if (report.Bands.Count == 0)
                report.Bands.Add(new FrxBand { Id = Guid.NewGuid(), ReportId = report.Id, BandType = "Detail", Height = 100, IsVisible = true });
            return template;
        }

        private static FrxBand? FindBand(IEnumerable<FrxBand> bands, double top)
        {
            return bands.FirstOrDefault(band => top >= band.Top && top <= band.Top + band.Height)
                   ?? bands.Where(band => band.Top <= top).OrderByDescending(band => band.Top).FirstOrDefault()
                   ?? bands.OrderBy(band => band.Top).FirstOrDefault();
        }

        private static string GetVfpBandType(int code) => code switch
        {
            0 => "Title", 1 => "PageHeader", 2 => "ColumnHeader", 3 => "GroupHeader",
            4 => "Detail", 5 => "GroupFooter", 6 => "ColumnFooter", 7 => "PageFooter",
            8 => "Summary", _ => $"Band{code}"
        };

        private static string GetVfpElementType(int objectType) => objectType switch
        {
            5 => "Text", 6 => "Line", 7 => "Box", 8 => "Expression", 17 => "Picture", _ => "Unknown"
        };

        private static string GetVfpAlignment(int value) => value switch
        {
            1 => "Right", 2 => "Center", _ => "Left"
        };

        private static int GetInt(IReadOnlyDictionary<string, object?> record, string name)
        {
            if (!record.TryGetValue(name, out var value) || value == null) return 0;
            return Convert.ToInt32(value);
        }

        private static double GetDouble(IReadOnlyDictionary<string, object?> record, string name, double fallback = 0)
        {
            if (!record.TryGetValue(name, out var value) || value == null) return fallback;
            return Convert.ToDouble(value);
        }

        private static string GetText(IReadOnlyDictionary<string, object?> record, string name, string fallback = "")
        {
            if (!record.TryGetValue(name, out var value) || value == null) return fallback;
            return value.ToString()?.Trim('\0', ' ') ?? fallback;
        }

        private sealed record VfpField(string Name, char Type, int Length, int DecimalCount);

        private FrxBand ParseBinaryBand(BinaryReader reader)
        {
            var band = new FrxBand
            {
                Id = Guid.NewGuid(),
                BandType = GetBandType(reader.ReadByte()),
                Top = reader.ReadInt16(),
                Height = reader.ReadInt16(),
                Left = reader.ReadInt16(),
                Width = reader.ReadInt16(),
                Order = reader.ReadInt16(),
                IsVisible = reader.ReadByte() != 0
            };

            // Пропускаем остальные данные
            reader.BaseStream.Seek(40, SeekOrigin.Current);

            return band;
        }

        private FrxField ParseBinaryField(BinaryReader reader)
        {
            var field = new FrxField
            {
                Id = Guid.NewGuid(),
                FieldName = ReadString(reader, 10),
                Expression = ReadString(reader, 254),
                Left = reader.ReadInt16(),
                Top = reader.ReadInt16(),
                Width = reader.ReadInt16(),
                Height = reader.ReadInt16(),
                Format = ReadString(reader, 50),
                FontName = ReadString(reader, 20),
                FontSize = reader.ReadByte(),
                FontBold = reader.ReadByte() != 0,
                Alignment = GetAlignment(reader.ReadByte()),
                Order = reader.ReadInt16()
            };

            return field;
        }

        private void CreateDefaultStructure(FrxReport report)
        {
            // Создаем стандартную структуру для ручного редактирования
            var bands = new[]
            {
                new FrxBand { BandType = "PageHeader", Height = 60, Order = 0, IsVisible = true },
                new FrxBand { BandType = "Header", Height = 30, Order = 1, IsVisible = true },
                new FrxBand { BandType = "Detail", Height = 25, Order = 2, IsVisible = true },
                new FrxBand { BandType = "Footer", Height = 30, Order = 3, IsVisible = true },
                new FrxBand { BandType = "PageFooter", Height = 30, Order = 4, IsVisible = true },
                new FrxBand { BandType = "Summary", Height = 40, Order = 5, IsVisible = true }
            };

            foreach (var band in bands)
            {
                band.ReportId = report.Id;
                report.Bands.Add(band);
            }
        }

        private string ReadString(BinaryReader reader, int length)
        {
            var bytes = reader.ReadBytes(length);
            var str = Encoding.ASCII.GetString(bytes);
            return str.TrimEnd('\0');
        }

        private string GetBandType(byte type)
        {
            return type switch
            {
                0x48 => "Header",
                0x44 => "Detail",
                0x46 => "Footer",
                0x50 => "PageHeader",
                0x51 => "PageFooter",
                0x53 => "Summary",
                _ => "Unknown"
            };
        }

        private string GetAlignment(byte align)
        {
            return align switch
            {
                0 => "Left",
                1 => "Right",
                2 => "Center",
                _ => "Left"
            };
        }

        private int GetIntAttribute(XmlNode node, string attr, int defaultValue = 0)
        {
            if (node.Attributes?[attr] != null && int.TryParse(node.Attributes[attr].Value, out int val))
                return val;
            return defaultValue;
        }

        private bool GetBoolAttribute(XmlNode node, string attr, bool defaultValue = false)
        {
            if (node.Attributes?[attr] != null && bool.TryParse(node.Attributes[attr].Value, out bool val))
                return val;
            return defaultValue;
        }

        private string GetStringAttribute(XmlNode node, string attr, string defaultValue = "")
        {
            return node.Attributes?[attr]?.Value ?? defaultValue;
        }
    }
}
