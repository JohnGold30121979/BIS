using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    public class FrxParser
    {
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