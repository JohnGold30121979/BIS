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
            var extension = Path.GetExtension(frxPath).ToLowerInvariant();
            var fileBytes = File.ReadAllBytes(frxPath);
            var fileText = TryReadText(fileBytes);
            if (extension is ".json" or ".fpx" || fileText.TrimStart().StartsWith("{") || fileText.TrimStart().StartsWith("["))
                return ParseFoxProJsonFile(frxPath, fileBytes, fileText);

            var report = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(frxPath),
                OriginalFileName = Path.GetFileName(frxPath),
                FrxData = fileBytes,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                var memoPath = Path.ChangeExtension(frxPath, ".FRT");
                var records = ReadVisualFoxProRecords(frxPath);
                var template = BuildVisualFoxProTemplateFull(report, records, frxPath);
                // Сохраняем полную структуру
                var fullData = new FrxFullData
                {
                    Template = template,
                    Bands = report.Bands.Select(b => new FrxSerializedBand
                    {
                        BandType = b.BandType,
                        Top = b.Top,
                        Height = b.Height,
                        Width = b.Width,
                        Left = b.Left,
                        Order = b.Order,
                        IsVisible = b.IsVisible,
                        FontName = b.FontName,
                        FontSize = b.FontSize,
                        FontBold = b.FontBold,
                        BackgroundColor = b.BackgroundColor,
                        Fields = report.Fields
                            .Where(f => f.BandId == b.Id)
                            .OrderBy(f => f.Order)
                            .Select(f => new FrxSerializedField
                            {
                                FieldName = f.FieldName,
                                Expression = f.Expression,
                                FieldType = f.FieldType,
                                DataSource = f.DataSource,
                                Left = f.Left,
                                Top = f.Top,
                                Width = f.Width,
                                Height = f.Height,
                                FontName = f.FontName,
                                FontSize = f.FontSize,
                                FontBold = f.FontBold,
                                Alignment = f.Alignment,
                                Format = f.Format,
                                BorderStyle = f.BorderStyle,
                                IsVisible = f.IsVisible,
                                Order = f.Order
                            }).ToList()
                    }).ToList()
                };
                report.FrxXml = JsonSerializer.Serialize(fullData, new JsonSerializerOptions { WriteIndented = false });
                report.ReportType = "FoxProLayout";
                report.Description = $"Импортирован из Visual FoxPro: {report.OriginalFileName}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FRX Parse Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack: {ex.StackTrace}");
                // Создаем структуру по умолчанию
                CreateDefaultStructure(report);
                var fallback = new PrintFormTemplate
                {
                    SourceFormat = "FoxProFRX",
                    OriginalFileName = report.OriginalFileName
                };
                report.FrxXml = JsonSerializer.Serialize(fallback);
                report.ReportType = "FoxProLayout";
                report.Description = $"Импортирован из Visual FoxPro (fallback): {report.OriginalFileName}";
            }

            return report;
        }

        private FrxReport ParseFoxProJsonFile(string filePath, byte[] fileBytes, string json)
        {
            using var document = JsonDocument.Parse(json);
            var report = new FrxReport
            {
                Id = Guid.NewGuid(),
                Name = Path.GetFileNameWithoutExtension(filePath),
                OriginalFileName = Path.GetFileName(filePath),
                FrxData = fileBytes,
                CreatedAt = DateTime.UtcNow,
                ReportType = "FoxProLayout",
                Description = $"Импортирован из FoxPro JSON: {Path.GetFileName(filePath)}"
            };

            // Пробуем сначала как FrxFullData (расширенный формат)
            if (TryDeserializeFullData(document.RootElement, out var fullData))
            {
                foreach (var serBand in fullData.Bands)
                {
                    var band = new FrxBand
                    {
                        Id = Guid.NewGuid(),
                        ReportId = report.Id,
                        BandType = serBand.BandType,
                        Top = serBand.Top,
                        Height = serBand.Height,
                        Width = serBand.Width,
                        Left = serBand.Left,
                        Order = serBand.Order,
                        IsVisible = serBand.IsVisible,
                        FontName = serBand.FontName,
                        FontSize = serBand.FontSize,
                        FontBold = serBand.FontBold,
                        BackgroundColor = serBand.BackgroundColor
                    };
                    report.Bands.Add(band);

                    foreach (var serField in serBand.Fields)
                    {
                        var field = new FrxField
                        {
                            Id = Guid.NewGuid(),
                            BandId = band.Id,
                            FieldName = serField.FieldName,
                            Expression = serField.Expression,
                            FieldType = serField.FieldType,
                            DataSource = serField.DataSource,
                            Left = serField.Left,
                            Top = serField.Top,
                            Width = serField.Width,
                            Height = serField.Height,
                            FontName = serField.FontName,
                            FontSize = serField.FontSize,
                            FontBold = serField.FontBold,
                            Alignment = serField.Alignment,
                            Format = serField.Format,
                            BorderStyle = serField.BorderStyle,
                            IsVisible = serField.IsVisible,
                            Order = serField.Order
                        };
                        report.Fields.Add(field);
                        band.Fields.Add(field);
                    }
                }

                if (fullData.Template != null && report.Bands.Count == 0)
                {
                    FillReportFromTemplate(report, fullData.Template);
                }
                report.FrxXml = JsonSerializer.Serialize(fullData);
                return report;
            }

            // Пробуем как PrintFormTemplate
            if (TryDeserializeTemplate(document.RootElement, out var readyTemplate))
            {
                FillReportFromTemplate(report, readyTemplate);
                report.FrxXml = JsonSerializer.Serialize(readyTemplate);
                return report;
            }

            // Пробуем как массив записей
            var records = ExtractJsonRecords(document.RootElement);
            if (records.Count == 0)
                throw new InvalidOperationException("JSON не содержит элементов макета FoxPro.");

            var template = BuildVisualFoxProTemplate(report, records);
            report.FrxXml = JsonSerializer.Serialize(template);
            return report;
        }

        private static bool TryDeserializeFullData(JsonElement root, out FrxFullData fullData)
        {
            fullData = null!;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (!root.TryGetProperty("Bands", out var bandsProp) || bandsProp.ValueKind != JsonValueKind.Array)
                return false;

            fullData = JsonSerializer.Deserialize<FrxFullData>(root.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new FrxFullData();
            return fullData.Bands.Count > 0;
        }

        private static bool TryDeserializeTemplate(JsonElement root, out PrintFormTemplate template)
        {
            template = new PrintFormTemplate();
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            if (root.TryGetProperty("Template", out _) || root.TryGetProperty("template", out _))
                return false;

            var hasTemplateShape =
                root.TryGetProperty("Elements", out _) ||
                root.TryGetProperty("elements", out _) ||
                root.TryGetProperty("Bands", out _) ||
                root.TryGetProperty("bands", out _);

            if (!hasTemplateShape)
                return false;

            template = JsonSerializer.Deserialize<PrintFormTemplate>(root.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PrintFormTemplate();
            return template.Elements.Count > 0 || template.Bands.Count > 0;
        }

        private static List<Dictionary<string, object?>> ExtractJsonRecords(JsonElement root)
        {
            var recordsElement = root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var propertyName in new[] { "records", "Records", "rows", "Rows", "data", "Data", "items", "Items" })
                {
                    if (root.TryGetProperty(propertyName, out var candidate) && candidate.ValueKind == JsonValueKind.Array)
                    {
                        recordsElement = candidate;
                        break;
                    }
                }
            }

            if (recordsElement.ValueKind != JsonValueKind.Array)
                return new List<Dictionary<string, object?>>();

            var records = new List<Dictionary<string, object?>>();
            foreach (var item in recordsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                    record[property.Name] = ReadJsonValue(property.Value);
                records.Add(record);
            }

            return records;
        }

        private static object? ReadJsonValue(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when value.TryGetInt32(out var intValue) => intValue,
                JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
                JsonValueKind.String => value.GetString(),
                _ => value.GetRawText()
            };
        }

        private static void FillReportFromTemplate(FrxReport report, PrintFormTemplate template)
        {
            var bandMap = new Dictionary<string, FrxBand>(StringComparer.OrdinalIgnoreCase);
            var order = 0;
            foreach (var templateBand in template.Bands.OrderBy(item => item.Order))
            {
                var band = new FrxBand
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    BandType = string.IsNullOrWhiteSpace(templateBand.Type) ? "Detail" : templateBand.Type,
                    Top = (int)Math.Round(templateBand.Top),
                    Height = Math.Max(1, (int)Math.Round(templateBand.Height)),
                    Order = order++,
                    IsVisible = true
                };
                report.Bands.Add(band);
                bandMap[band.BandType] = band;
            }

            if (report.Bands.Count == 0)
            {
                var detail = new FrxBand { Id = Guid.NewGuid(), ReportId = report.Id, BandType = "Detail", Height = 25, IsVisible = true };
                report.Bands.Add(detail);
                bandMap[detail.BandType] = detail;
            }

            order = 0;
            foreach (var element in template.Elements.OrderBy(item => item.Order))
            {
                if (!bandMap.TryGetValue(element.BandType, out var band))
                    band = report.Bands.First();

                var field = new FrxField
                {
                    Id = Guid.NewGuid(),
                    BandId = band.Id,
                    FieldName = string.IsNullOrWhiteSpace(element.Text) ? element.Expression : element.Text,
                    Expression = element.Expression,
                    FieldType = string.IsNullOrWhiteSpace(element.Type) ? "Text" : element.Type,
                    Left = (int)Math.Round(element.Left),
                    Top = (int)Math.Round(element.Top),
                    Width = Math.Max(1, (int)Math.Round(element.Width)),
                    Height = Math.Max(1, (int)Math.Round(element.Height)),
                    FontName = string.IsNullOrWhiteSpace(element.FontName) ? "Arial" : element.FontName,
                    FontSize = Math.Max(1, (int)Math.Round(element.FontSize)),
                    FontBold = element.Bold,
                    Alignment = string.IsNullOrWhiteSpace(element.Alignment) ? "Left" : element.Alignment,
                    BorderStyle = string.IsNullOrWhiteSpace(element.BorderStyle) ? "None" : element.BorderStyle,
                    IsVisible = true,
                    Order = order++
                };
                report.Fields.Add(field);
                band.Fields.Add(field);
            }
        }

        private static string TryReadText(byte[] data)
        {
            try
            {
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        public PrintFormTemplate GetPrintTemplate(FrxReport report)
        {
            if (string.IsNullOrWhiteSpace(report.FrxXml))
                return new PrintFormTemplate { OriginalFileName = report.OriginalFileName };

            try
            {
                using var document = JsonDocument.Parse(report.FrxXml);
                if (TryDeserializeTemplate(document.RootElement, out var readyTemplate))
                {
                    if (string.IsNullOrWhiteSpace(readyTemplate.OriginalFileName))
                        readyTemplate.OriginalFileName = report.OriginalFileName;
                    return readyTemplate;
                }
            }
            catch { }

            try
            {
                // Пробуем десериализовать как FrxFullData после прямого PrintFormTemplate:
                // у нативного шаблона тоже есть Bands, поэтому обратный порядок теряет Elements.
                var fullData = JsonSerializer.Deserialize<FrxFullData>(report.FrxXml,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (fullData?.Template != null)
                    return fullData.Template;
                if (fullData?.Bands.Count > 0)
                {
                    // Конвертируем расширенные данные в шаблон
                    var template = new PrintFormTemplate
                    {
                        SourceFormat = "FoxProFRX",
                        OriginalFileName = report.OriginalFileName,
                        PageWidth = report.Bands.Any() ? report.Bands.Max(b => b.Left + b.Width) + 100 : 2100,
                        PageHeight = report.Bands.Any() ? report.Bands.Max(b => b.Top + b.Height) + 50 : 2970
                    };

                    foreach (var band in fullData.Bands)
                    {
                        template.Bands.Add(new PrintFormBand
                        {
                            Type = band.BandType,
                            Top = band.Top,
                            Height = band.Height,
                            Order = band.Order
                        });
                        foreach (var field in band.Fields)
                        {
                            template.Elements.Add(new PrintFormElement
                            {
                                Type = field.FieldType,
                                Text = field.FieldType == "Text" ? field.FieldName : string.Empty,
                                Expression = field.Expression,
                                BandType = band.BandType,
                                Left = field.Left,
                                Top = field.Top,
                                Width = field.Width,
                                Height = field.Height,
                                FontName = field.FontName,
                                FontSize = field.FontSize,
                                Bold = field.FontBold,
                                Alignment = field.Alignment,
                                BorderStyle = field.BorderStyle,
                                Order = field.Order
                            });
                        }
                    }

                    if (template.Elements.Count > 0)
                    {
                        template.PageWidth = Math.Max(template.PageWidth,
                            template.Elements.Max(e => e.Left + e.Width) + 100);
                        template.PageHeight = Math.Max(template.PageHeight,
                            template.Elements.Max(e => e.Top + e.Height) + 100);
                    }

                    return template;
                }
            }
            catch { }

            // Fallback к старому формату
            try
            {
                return JsonSerializer.Deserialize<PrintFormTemplate>(report.FrxXml) ?? new PrintFormTemplate();
            }
            catch
            {
                return new PrintFormTemplate { OriginalFileName = report.OriginalFileName };
            }
        }

        /// <summary>
        /// Получить полные данные отчета из FrxXml
        /// </summary>
        public FrxFullData GetFullData(FrxReport report)
        {
            if (string.IsNullOrWhiteSpace(report.FrxXml))
            {
                // Создаем из текущих данных отчета
                var template = new PrintFormTemplate
                {
                    SourceFormat = "FoxProFRX",
                    OriginalFileName = report.OriginalFileName
                };
                return new FrxFullData
                {
                    Template = template,
                    Bands = report.Bands.Select(b => new FrxSerializedBand
                    {
                        BandType = b.BandType,
                        Top = b.Top,
                        Height = b.Height,
                        Width = b.Width,
                        Left = b.Left,
                        Order = b.Order,
                        IsVisible = b.IsVisible,
                        FontName = b.FontName,
                        FontSize = b.FontSize,
                        FontBold = b.FontBold,
                        BackgroundColor = b.BackgroundColor,
                        Fields = report.Fields
                            .Where(f => f.BandId == b.Id)
                            .OrderBy(f => f.Order)
                            .Select(f => new FrxSerializedField
                            {
                                FieldName = f.FieldName,
                                Expression = f.Expression,
                                FieldType = f.FieldType,
                                DataSource = f.DataSource,
                                Left = f.Left,
                                Top = f.Top,
                                Width = f.Width,
                                Height = f.Height,
                                FontName = f.FontName,
                                FontSize = f.FontSize,
                                FontBold = f.FontBold,
                                Alignment = f.Alignment,
                                Format = f.Format,
                                BorderStyle = f.BorderStyle,
                                IsVisible = f.IsVisible,
                                Order = f.Order
                            }).ToList()
                    }).ToList()
                };
            }

            try
            {
                return JsonSerializer.Deserialize<FrxFullData>(report.FrxXml,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new FrxFullData();
            }
            catch
            {
                return new FrxFullData();
            }
        }

        public FrxReport ParseFrxFile(byte[] frxData, string fileName)
        {
            // Временная переадресация - сохраняем во временный файл
            var tempPath = Path.Combine(Path.GetTempPath(), fileName);
            File.WriteAllBytes(tempPath, frxData);
            try
            {
                return ParseFrxFile(tempPath);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
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

            // Проверяем сигнатуру DBF
            if (dbf.Length < 32)
                throw new InvalidDataException("Файл слишком мал для формата DBF");

            // Версия DBF (первый байт)
            var dbfVersion = dbf[0];

            var recordCount = BinaryPrimitives.ReadInt32LittleEndian(dbf.AsSpan(4, 4));
            var headerLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf.AsSpan(8, 2));
            var recordLength = BinaryPrimitives.ReadUInt16LittleEndian(dbf.AsSpan(10, 2));

            System.Diagnostics.Debug.WriteLine($"DBF: version=0x{dbfVersion:X2}, records={recordCount}, headerLen={headerLength}, recordLen={recordLength}");

            // Читаем поля из заголовка
            var fields = new List<VfpField>();
            var fieldOffset = 32;
            while (fieldOffset + 32 <= headerLength)
            {
                var descriptor = dbf.AsSpan(fieldOffset, 32);

                // Проверка на конец описания полей (0x0D)
                if (descriptor[0] == 0x0D)
                    break;

                var nameEnd = descriptor[..11].IndexOf((byte)0);
                if (nameEnd < 0) nameEnd = 11;

                var fieldName = Encoding.ASCII.GetString(descriptor[..nameEnd]);
                var fieldType = (char)descriptor[11];
                var fieldLength = descriptor[16];
                var fieldDecimal = descriptor[17];

                System.Diagnostics.Debug.WriteLine($"  Field: {fieldName} type={fieldType} len={fieldLength} dec={fieldDecimal}");

                fields.Add(new VfpField(fieldName, fieldType, fieldLength, fieldDecimal));
                fieldOffset += 32;
            }

            var memoBlockSize = memo.Length >= 8
                ? BinaryPrimitives.ReadUInt16BigEndian(memo.AsSpan(6, 2))
                : 0;

            // Для FPT memo блок = 512 (более распространено)
            if (memoBlockSize == 0 && memo.Length > 0)
                memoBlockSize = 512;

            var records = new List<Dictionary<string, object?>>();
            for (var recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                var recordOffset = headerLength + recordIndex * recordLength;
                if (recordOffset + recordLength > dbf.Length || dbf[recordOffset] == (byte)'*')
                    continue;

                var record = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                var rowOffset = recordOffset + 1; // +1 для байта удаления

                foreach (var field in fields)
                {
                    var raw = dbf.AsSpan(rowOffset, field.Length);
                    rowOffset += field.Length;

                    object? value = field.Type switch
                    {
                        'M' => ReadMemo(raw, memo, memoBlockSize, encoding),
                        'N' or 'F' => ReadNumber(raw, encoding),
                        'L' => raw.Length > 0 && raw[0] is (byte)'T' or (byte)'t' or (byte)'Y' or (byte)'y',
                        'D' => ReadDate(raw, encoding),
                        'T' => ReadDateTime(raw),
                        'I' => raw.Length >= 4 ? BinaryPrimitives.ReadInt32LittleEndian(raw) : (object?)null,
                        'B' => ReadDouble(raw),
                        _ => encoding.GetString(raw).Trim('\0', ' ')
                    };

                    record[field.Name] = value;
                }

                records.Add(record);
            }

            System.Diagnostics.Debug.WriteLine($"Прочитано записей из DBF: {records.Count}");
            return records;
        }

        private static string ReadDate(ReadOnlySpan<byte> raw, Encoding encoding)
        {
            var text = encoding.GetString(raw).Trim('\0', ' ');
            if (text.Length == 8) // YYYYMMDD
            {
                if (DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return date.ToString("yyyy-MM-dd");
            }
            return text;
        }

        private static DateTime? ReadDateTime(ReadOnlySpan<byte> raw)
        {
            if (raw.Length < 8) return null;
            var julianDays = BinaryPrimitives.ReadInt32LittleEndian(raw);
            var msSinceMidnight = BinaryPrimitives.ReadInt32LittleEndian(raw[4..]);
            if (julianDays == 0) return null;
            try
            {
                // Julian day to DateTime (JDN for 4713 BC to 9999 AD)
                var jd = julianDays;
                var z = jd;
                var alpha = (int)((z - 1867216.25) / 36524.25);
                var a = z + 1 + alpha - (int)(alpha / 4);
                var b = a + 1524;
                var c = (int)((b - 122.1) / 365.25);
                var d = (int)(365.25 * c);
                var e = (int)((b - d) / 30.6001);
                var day = b - d - (int)(30.6001 * e);
                var month = e <= 13 ? e - 1 : e - 13;
                var year = month <= 2 ? c - 4715 : c - 4716;
                return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(msSinceMidnight);
            }
            catch
            {
                return null;
            }
        }

        private static double? ReadDouble(ReadOnlySpan<byte> raw)
        {
            if (raw.Length < 8) return null;
            return BinaryPrimitives.ReadDoubleLittleEndian(raw);
        }

        private static string ReadMemo(ReadOnlySpan<byte> pointerBytes, byte[] memo, int blockSize, Encoding encoding)
        {
            if (pointerBytes.Length < 4 || memo.Length == 0 || blockSize <= 0)
                return string.Empty;

            var block = BinaryPrimitives.ReadInt32LittleEndian(pointerBytes[..4]);
            if (block <= 0)
                return string.Empty;

            var offset = block * blockSize;
            if (offset < 0 || offset + 8 > memo.Length)
                return string.Empty;

            // Тип memo блока: 0x00000001 - текст, 0x00000000 - изображение
            var memoType = BinaryPrimitives.ReadInt32BigEndian(memo.AsSpan(offset, 4));
            var length = BinaryPrimitives.ReadInt32BigEndian(memo.AsSpan(offset + 4, 4));

            if (length <= 0 || offset + 8 + length > memo.Length)
                return string.Empty;

            // Для текстовых memo блоков (тип 1) читаем как текст
            if (memoType == 1)
            {
                return encoding.GetString(memo, offset + 8, length).Trim('\0', ' ');
            }

            return encoding.GetString(memo, offset + 8, length).Trim('\0', ' ');
        }

        private static object? ReadNumber(ReadOnlySpan<byte> raw, Encoding encoding)
        {
            var text = encoding.GetString(raw).Trim();
            if (string.IsNullOrWhiteSpace(text)) return null;
            return decimal.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var number)
                ? number
                : text;
        }

        /// <summary>
        /// Расширенная версия сборки шаблона - сохраняет ВСЕ поля, включая поля с неизвестными OBJTYPE
        /// </summary>
        private PrintFormTemplate BuildVisualFoxProTemplateFull(
            FrxReport report,
            IReadOnlyCollection<Dictionary<string, object?>> records,
            string frxPath)
        {
            // Используем оригинальный метод для базовой сборки
            return BuildVisualFoxProTemplate(report, records);
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

            // 1. Находим запись отчета (OBJTYPE = 1)
            var reportRecord = records.FirstOrDefault(record => GetInt(record, "OBJTYPE") == 1);
            if (reportRecord != null)
            {
                template.PageWidth = Math.Max(1, GetDouble(reportRecord, "WIDTH"));
                template.PageHeight = Math.Max(1, GetDouble(reportRecord, "HEIGHT"));
                System.Diagnostics.Debug.WriteLine($"Report: WIDTH={template.PageWidth}, HEIGHT={template.PageHeight}");
            }

            // 2. Парсим все банды (OBJTYPE = 9)
            var bandRecords = records.Where(record => GetInt(record, "OBJTYPE") == 9)
                .OrderBy(record => GetDouble(record, "VPOS")).ToList();
            System.Diagnostics.Debug.WriteLine($"Найдено банд: {bandRecords.Count}");

            var bandOrder = 0;
            var bandMap = new Dictionary<int, FrxBand>(); // by OBJCODE for group headers/footers

            foreach (var record in bandRecords)
            {
                var bandCode = GetInt(record, "OBJCODE");
                var bandType = GetVfpBandType(bandCode);
                var band = new FrxBand
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    BandType = bandType,
                    Top = (int)Math.Round(GetDouble(record, "VPOS")),
                    Height = Math.Max(1, (int)Math.Round(GetDouble(record, "HEIGHT"))),
                    Width = Math.Max(1, (int)Math.Round(GetDouble(record, "WIDTH"))),
                    Left = (int)Math.Round(GetDouble(record, "HPOS")),
                    Order = bandOrder++,
                    IsVisible = true
                };
                report.Bands.Add(band);
                bandMap[bandCode] = band;
                template.Bands.Add(new PrintFormBand
                {
                    Type = band.BandType,
                    Top = band.Top,
                    Height = band.Height,
                    Order = band.Order
                });

                System.Diagnostics.Debug.WriteLine($"  Band: {bandType} (code={bandCode}) top={band.Top} height={band.Height}");
            }

            // 3. Парсим все элементы макета
            // В FoxPro FRX, OBJTYPE может быть:
            //   5 = Label (текстовая метка)
            //   6 = Line (линия)
            //   7 = Box (прямоугольник)
            //   8 = Field (поле с выражением)
            //  17 = Picture/OLE (изображение)
            //  10 = Group (группа)
            //  12 = Subreport (вложенный отчет)
            //  14 = Variable (переменная)
            var elementRecords = records
                .Where(record =>
                {
                    var objType = GetInt(record, "OBJTYPE");
                    return objType is 5 or 6 or 7 or 8 or 10 or 12 or 14 or 17 or 0;
                })
                .OrderBy(record => GetDouble(record, "VPOS"))
                .ThenBy(record => GetDouble(record, "HPOS"))
                .ToList();

            System.Diagnostics.Debug.WriteLine($"Найдено элементов макета: {elementRecords.Count}");

            var fieldOrder = 0;

            // Группировка записей по EXPR для полей (OBJTYPE 8) - многие поля могут ссылаться 
            // на одно и то же выражение через DATAHANDLE
            var expressionCache = new Dictionary<int, string>();

            foreach (var record in elementRecords)
            {
                var objectType = GetInt(record, "OBJTYPE");
                var top = GetDouble(record, "VPOS");
                var left = GetDouble(record, "HPOS");

                // Находим банду по координатам
                var band = FindBandByPosition(report.Bands, top);

                if (band == null && report.Bands.Count > 0)
                    band = report.Bands.First();

                if (band == null)
                {
                    // Создаем Detail банду если нет ни одной
                    band = new FrxBand
                    {
                        Id = Guid.NewGuid(),
                        ReportId = report.Id,
                        BandType = "Detail",
                        Height = (int)Math.Round(GetDouble(record, "HEIGHT", 20)),
                        Order = bandOrder++,
                        IsVisible = true
                    };
                    report.Bands.Add(band);
                    template.Bands.Add(new PrintFormBand { Type = "Detail", Top = band.Top, Height = band.Height, Order = band.Order });
                }

                // Получаем EXPR - может быть числовым DATAHANDLE (ссылка на FRT memo)
                var exprRaw = GetText(record, "EXPR");
                var expression = exprRaw;

                // Если EXPR выглядит как число, это может быть DATAHANDLE на FRT
                if (int.TryParse(exprRaw, out var dataHandle) && dataHandle > 0)
                {
                    if (expressionCache.TryGetValue(dataHandle, out var cachedExpr))
                    {
                        expression = cachedExpr;
                    }
                    else
                    {
                        // Ищем в других записях совпадение по DATAHANDLE
                        var exprRecord = records.FirstOrDefault(r =>
                            GetInt(r, "DATAHANDLE") == dataHandle &&
                            GetInt(r, "OBJTYPE") == 8);
                        if (exprRecord != null)
                        {
                            var exprText = GetText(exprRecord, "EXPR");
                            if (!string.IsNullOrWhiteSpace(exprText))
                            {
                                expression = exprText;
                                expressionCache[dataHandle] = exprText;
                            }
                        }
                    }
                }

                // Обработка USER - хранит дополнительные свойства полей
                var userValue = GetText(record, "USER");

                // Определяем имя поля - для OBJTYPE 8 (Field) берем NAME, для 5 (Label) берем EXPR как текст
                string fieldName;
                if (objectType == 5)
                {
                    // Label (текстовая метка) - EXPR содержит отображаемый текст
                    fieldName = expression;
                }
                else if (objectType == 8)
                {
                    // Field (поле с выражением) - NAME содержит имя, EXPR содержит выражение
                    fieldName = GetText(record, "NAME");
                    if (string.IsNullOrWhiteSpace(fieldName))
                        fieldName = $"Field{fieldOrder}";
                }
                else
                {
                    fieldName = GetText(record, "NAME");
                    if (string.IsNullOrWhiteSpace(fieldName))
                        fieldName = $"{GetVfpElementType(objectType)}{fieldOrder}";
                }

                var fillChar = GetText(record, "FILLCHAR");
                var fontStyle = GetInt(record, "FONTSTYLE");
                var offset = GetInt(record, "OFFSET");

                // Пропускаем элементы с нулевыми размерами (обычно это скрытые элементы)
                var width = Math.Max(1, (int)Math.Round(GetDouble(record, "WIDTH")));
                var height = Math.Max(1, (int)Math.Round(GetDouble(record, "HEIGHT")));

                var field = new FrxField
                {
                    Id = Guid.NewGuid(),
                    BandId = band.Id,
                    FieldName = fieldName,
                    Expression = expression,
                    FieldType = GetVfpElementType(objectType),
                    DataSource = GetText(record, "PICTURE"),
                    Left = (int)Math.Round(left),
                    Top = (int)Math.Round(top),
                    Width = width,
                    Height = height,
                    FontName = GetText(record, "FONTFACE", "Arial"),
                    FontSize = Math.Max(1, (int)Math.Round(GetDouble(record, "FONTSIZE", 9))),
                    FontBold = (fontStyle & 1) == 1,
                    FontItalic = (fontStyle & 2) == 2,
                    FontUnderline = (fontStyle & 4) == 4,
                    Alignment = GetVfpAlignment(offset),
                    Format = userValue,
                    BorderStyle = objectType is 6 or 7 ? "Solid" : "None",
                    IsVisible = true,
                    Order = fieldOrder++
                };

                report.Fields.Add(field);
                band.Fields.Add(field);

                template.Elements.Add(new PrintFormElement
                {
                    Type = field.FieldType,
                    Text = objectType == 5 ? expression : string.Empty,
                    Expression = field.Expression,
                    BandType = band.BandType,
                    Left = field.Left,
                    Top = field.Top,
                    Width = field.Width,
                    Height = field.Height,
                    FontName = field.FontName,
                    FontSize = field.FontSize,
                    Bold = field.FontBold,
                    Italic = field.FontItalic,
                    Alignment = field.Alignment,
                    BorderStyle = field.BorderStyle,
                    Order = field.Order
                });

                System.Diagnostics.Debug.WriteLine(
                    $"  Field[{fieldOrder - 1}]: type={objectType}({field.FieldType}) " +
                    $"name='{field.FieldName}' expr='{Truncate(field.Expression, 30)}' " +
                    $"pos=({field.Left},{field.Top}) size=({field.Width}x{field.Height}) " +
                    $"band={band.BandType} font={field.FontName}/{field.FontSize}");
            }

            // 4. Парсим переменные отчета (OBJTYPE = 14 или записи с TYPE = "V" или записи с OBJTYPE = 10)
            var variableRecords = records.Where(record =>
                GetInt(record, "OBJTYPE") == 14 ||
                GetText(record, "TYPE").Equals("V", StringComparison.OrdinalIgnoreCase) ||
                GetInt(record, "OBJTYPE") == 10)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"Найдено переменных/групп: {variableRecords.Count}");

            foreach (var record in variableRecords)
            {
                var exprText = GetText(record, "EXPR");
                var varName = GetText(record, "NAME");
                if (!string.IsNullOrWhiteSpace(varName) && !string.IsNullOrWhiteSpace(exprText))
                {
                    report.Expressions.Add(new FrxExpression
                    {
                        Id = Guid.NewGuid(),
                        ReportId = report.Id,
                        Name = varName,
                        Expression = exprText,
                        Order = report.Expressions.Count
                    });
                }
            }

            // 5. Настраиваем размер страницы по элементам
            if (template.Elements.Count > 0)
            {
                var maxRight = template.Elements.Max(element => element.Left + element.Width);
                var maxBottom = template.Elements.Max(element => element.Top + element.Height);
                template.PageWidth = Math.Max(template.PageWidth, maxRight + 500);
                template.PageHeight = Math.Max(template.PageHeight, maxBottom + 500);
            }

            // 6. Если нет банд, создаем Detail
            if (report.Bands.Count == 0)
            {
                var defaultBand = new FrxBand
                {
                    Id = Guid.NewGuid(),
                    ReportId = report.Id,
                    BandType = "Detail",
                    Height = 100,
                    IsVisible = true
                };
                report.Bands.Add(defaultBand);
                template.Bands.Add(new PrintFormBand { Type = "Detail", Height = 100, Order = 0 });
            }

            System.Diagnostics.Debug.WriteLine($"Итого: банд={report.Bands.Count}, полей={report.Fields.Count}, выражений={report.Expressions.Count}");

            return template;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maxLength ? value : value[..maxLength] + "...";
        }

        /// <summary>
        /// Находит банду, которой принадлежит элемент по его вертикальной позиции
        /// Улучшенная версия - учитывает порядок банд и их перекрытие
        /// </summary>
        private static FrxBand? FindBandByPosition(IEnumerable<FrxBand> bands, double top)
        {
            var sortedBands = bands.OrderBy(b => b.Order).ToList();
            if (sortedBands.Count == 0) return null;

            // Ищем банду, которая точно содержит эту позицию
            var exactMatch = sortedBands.FirstOrDefault(band => top >= band.Top && top < band.Top + band.Height);
            if (exactMatch != null) return exactMatch;

            // Ищем банду, которая начинается до этой позиции и заканчивается после
            var containingBand = sortedBands
                .Where(band => band.Top <= top && (band.Top + band.Height) > top)
                .OrderByDescending(band => band.Top)
                .FirstOrDefault();
            if (containingBand != null) return containingBand;

            // Ищем ближайшую банду сверху
            var previousBand = sortedBands
                .Where(band => band.Top <= top)
                .OrderByDescending(band => band.Top)
                .FirstOrDefault();

            if (previousBand != null)
            {
                // Проверяем, что элемент не слишком далеко от банды
                var gap = top - (previousBand.Top + previousBand.Height);
                if (gap < 50) // Допускаем небольшой зазор
                    return previousBand;
            }

            // Ищем ближайшую банду снизу
            var nextBand = sortedBands
                .Where(band => band.Top > top)
                .OrderBy(band => band.Top)
                .FirstOrDefault();

            if (nextBand != null && nextBand.Top - top < 20)
                return nextBand;

            // Возвращаем банду Detail если есть, иначе первую
            return sortedBands.FirstOrDefault(b => b.BandType == "Detail") ?? sortedBands.First();
        }

        private static FrxBand? FindBand(IEnumerable<FrxBand> bands, double top)
        {
            return FindBandByPosition(bands, top);
        }

        private static string GetVfpBandType(int code) => code switch
        {
            0 => "Title",
            1 => "PageHeader",
            2 => "ColumnHeader",
            3 => "GroupHeader",
            4 => "Detail",
            5 => "GroupFooter",
            6 => "ColumnFooter",
            7 => "PageFooter",
            8 => "Summary",
            _ => $"Band{code}"
        };

        private static string GetVfpElementType(int objectType) => objectType switch
        {
            5 => "Text",
            6 => "Line",
            7 => "Box",
            8 => "Expression",
            10 => "Group",
            12 => "Subreport",
            14 => "Variable",
            17 => "Picture",
            _ => "Unknown"
        };

        private static string GetVfpAlignment(int value) => value switch
        {
            1 => "Right",
            2 => "Center",
            _ => "Left"
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

    /// <summary>
    /// Расширенный класс для сериализации полной структуры FRX отчета
    /// </summary>
    public class FrxFullData
    {
        public PrintFormTemplate? Template { get; set; }
        public List<FrxSerializedBand> Bands { get; set; } = new();
    }

    public class FrxSerializedBand
    {
        public string BandType { get; set; } = "Detail";
        public int Top { get; set; }
        public int Height { get; set; }
        public int Width { get; set; }
        public int Left { get; set; }
        public int Order { get; set; }
        public bool IsVisible { get; set; } = true;
        public string FontName { get; set; } = "Segoe UI";
        public int FontSize { get; set; } = 10;
        public bool FontBold { get; set; }
        public string BackgroundColor { get; set; } = "#FFFFFF";
        public List<FrxSerializedField> Fields { get; set; } = new();
    }

    public class FrxSerializedField
    {
        public string FieldName { get; set; } = string.Empty;
        public string Expression { get; set; } = string.Empty;
        public string FieldType { get; set; } = "Text";
        public string DataSource { get; set; } = string.Empty;
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; } = 100;
        public int Height { get; set; } = 20;
        public string FontName { get; set; } = "Arial";
        public int FontSize { get; set; } = 9;
        public bool FontBold { get; set; }
        public bool FontItalic { get; set; }
        public bool FontUnderline { get; set; }
        public string Alignment { get; set; } = "Left";
        public string Format { get; set; } = string.Empty;
        public string BorderStyle { get; set; } = "None";
        public bool IsVisible { get; set; } = true;
        public int Order { get; set; }
    }
}
