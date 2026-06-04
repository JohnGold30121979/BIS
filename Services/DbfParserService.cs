using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class DbfParserService
    {
        public async Task<DbfParseResult> ParseDbfFileAsync(string filePath)
        {
            var result = new DbfParseResult();

            await Task.Run(() =>
            {
                try
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                    using (var reader = new BinaryReader(fs))
                    {
                        // Количество записей
                        fs.Seek(4, SeekOrigin.Begin);
                        result.RecordCount = reader.ReadInt32();

                        // Длина заголовка
                        fs.Seek(8, SeekOrigin.Begin);
                        var headerLength = reader.ReadInt16();

                        // Длина записи
                        fs.Seek(10, SeekOrigin.Begin);
                        var recordLength = reader.ReadInt16();

                        // Читаем ВСЕ поля
                        var fields = new List<DbfFieldInfo>();
                        fs.Seek(32, SeekOrigin.Begin);

                        while (true)
                        {
                            var fieldNameBytes = reader.ReadBytes(11);
                            var fieldName = Encoding.ASCII.GetString(fieldNameBytes).TrimEnd('\0');

                            if (string.IsNullOrEmpty(fieldName) || fieldNameBytes[0] == 0x0D)
                                break;

                            var fieldType = (char)reader.ReadByte();
                            reader.ReadInt32(); // смещение
                            var fieldLength = reader.ReadByte();
                            var fieldDecimal = reader.ReadByte();
                            reader.ReadBytes(14); // резерв

                            fields.Add(new DbfFieldInfo
                            {
                                Name = fieldName,
                                Type = fieldType.ToString(),
                                Length = fieldLength,
                                DecimalCount = fieldDecimal
                            });
                        }

                        result.Fields = fields;
                        result.TableName = Path.GetFileNameWithoutExtension(filePath);

                        // Читаем данные
                        var encoding = Encoding.GetEncoding(1251);
                        result.Rows = new List<Dictionary<string, object>>();
                        var dataStart = headerLength;

                        for (int i = 0; i < result.RecordCount; i++)
                        {
                            var rowData = reader.ReadBytes(recordLength);
                            var row = new Dictionary<string, object>();
                            var offset = 0;

                            foreach (var field in fields)
                            {
                                if (offset + field.Length <= rowData.Length)
                                {
                                    var valueBytes = new byte[field.Length];
                                    Array.Copy(rowData, offset, valueBytes, 0, field.Length);
                                    var value = encoding.GetString(valueBytes).TrimEnd('\0').Trim();

                                    // Преобразуем типы
                                    if (field.Type == "N" && !string.IsNullOrEmpty(value))
                                    {
                                        var numStr = value.Replace('.', ',');
                                        if (decimal.TryParse(numStr, out decimal dec))
                                            row[field.Name] = dec;
                                        else
                                            row[field.Name] = value;
                                    }
                                    else
                                    {
                                        row[field.Name] = string.IsNullOrEmpty(value) ? null : value;
                                    }
                                }
                                offset += field.Length;
                            }

                            result.Rows.Add(row);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.HasError = true;
                    result.ErrorMessage = ex.Message;
                }
            });

            return result;
        }

        private string CleanString(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            var result = new StringBuilder();
            foreach (char c in input)
            {
                if (c != '\0' && c != '\u001A' && c != '\uFFFD')
                {
                    result.Append(c);
                }
            }
            return result.ToString();
        }
    }

    public class DbfParseResult
    {
        public string TableName { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public List<DbfFieldInfo> Fields { get; set; } = new List<DbfFieldInfo>();
        public List<Dictionary<string, object>> Rows { get; set; } = new List<Dictionary<string, object>>();
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class DbfFieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Length { get; set; }
        public int DecimalCount { get; set; }
        public int Index { get; set; }
    }
}