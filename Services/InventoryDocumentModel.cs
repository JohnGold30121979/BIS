using System;
using System.Collections.Generic;
using BIS.ERP.Models;

namespace BIS.ERP.Services
{
    /// <summary>
    /// Модель документов ТМЗ (товарно-материальных запасов).
    ///
    /// Решение от 20.10.2026 (по эталонной программе): вместо отдельной таблицы
    /// на каждый вид операции — три документа, а вид операции хранится реквизитом:
    ///
    /// <list type="bullet">
    /// <item><b>Поступление ТМЗ</b> — приобретение, приход из производства, приход по акту;</item>
    /// <item><b>Выбытие ТМЗ</b> — реализация, расход в производство, списание по акту;</item>
    /// <item><b>Внутреннее перемещение ТМЗ</b> — передача в подотчёт, автоматическая передача.</item>
    /// </list>
    ///
    /// Окно документа повторяет счёт-фактуру: шапка (номер, дата, ЭСФ, счёт, организация,
    /// сотрудник, виды налога/оплаты/покупки, валюта, итоги) + таблица строк
    /// (материал, единица, дебет, участок, МОЛ, количество, цена, суммы).
    /// Строки — отдельная таблица <c>&lt;шапка&gt;_lines</c>, как в финансах.
    /// </summary>
    public static class InventoryDocumentModel
    {
        public const string IncomingName = "Поступление ТМЗ";
        public const string OutgoingName = "Выбытие ТМЗ";
        public const string MovementName = "Внутреннее перемещение ТМЗ";

        public const string IncomingTable = "doc_inventory_incoming";
        public const string OutgoingTable = "doc_inventory_outgoing";
        public const string MovementTable = "doc_inventory_movement";

        public const string IncomingLinesTable = "doc_inventory_incoming_lines";
        public const string OutgoingLinesTable = "doc_inventory_outgoing_lines";
        public const string MovementLinesTable = "doc_inventory_movement_lines";

        public const string EntryCatalogName = "Ввод нового документа ТМЗ";
        public const string EntryCatalogTable = "catalog_inventory_document_entries";

        public const string MaterialCatalogName = "Справочник материалов";
        public const string ChartOfAccountsCatalogName = "План счетов";

        /// <summary>Документ ТМЗ: имя, шапка, строки, колонка-родитель в строках.</summary>
        public sealed class DocumentDefinition
        {
            public DocumentDefinition(
                string name,
                string table,
                string linesTable,
                string parentColumn,
                string icon,
                int order,
                string description,
                string[] legacyNames)
            {
                Name = name;
                Table = table;
                LinesTable = linesTable;
                ParentColumn = parentColumn;
                Icon = icon;
                Order = order;
                Description = description;
                LegacyNames = legacyNames;
            }

            public string Name { get; }
            public string Table { get; }
            public string LinesTable { get; }
            public string ParentColumn { get; }
            public string Icon { get; }
            public int Order { get; }
            public string Description { get; }

            /// <summary>Прежние имена документа, которые переименовываются в это.</summary>
            public string[] LegacyNames { get; }
        }

        public static IReadOnlyList<DocumentDefinition> Documents { get; } = new[]
        {
            new DocumentDefinition(
                IncomingName, IncomingTable, IncomingLinesTable, "incoming_id", "📥", 1,
                "Поступление товарно-материальных запасов на склад",
                new[] { "Приход товаров" }),
            new DocumentDefinition(
                OutgoingName, OutgoingTable, OutgoingLinesTable, "outgoing_id", "📤", 2,
                "Выбытие товарно-материальных запасов со склада",
                new[] { "Расход товаров" }),
            new DocumentDefinition(
                MovementName, MovementTable, MovementLinesTable, "movement_id", "🔁", 3,
                "Внутреннее перемещение товарно-материальных запасов",
                new[] { "Внутреннее перемещение ТМЦ" })
        };

        /// <summary>Вид операции: код, название, документ, группа движения, порядок.</summary>
        public sealed class KindDefinition
        {
            public KindDefinition(
                string code, string name, string documentName, string movementKind, int order, string description,
                string? operationKind = null)
            {
                Code = code;
                Name = name;
                DocumentName = documentName;
                MovementKind = movementKind;
                Order = order;
                Description = description;
                OperationKind = string.IsNullOrWhiteSpace(operationKind) ? code : operationKind;
            }

            public string Code { get; }
            public string Name { get; }
            public string DocumentName { get; }
            public string MovementKind { get; }
            public int Order { get; }
            public string Description { get; }

            /// <summary>Код операции, который пишется в шапку документа.</summary>
            public string OperationKind { get; }
        }

        /// <summary>
        /// Восемь видов операций — состав диалога «Ввод нового документа» эталонной программы.
        /// </summary>
        public static IReadOnlyList<KindDefinition> Kinds { get; } = new[]
        {
            new KindDefinition("INCOMING_PURCHASE", "Приобретение ТМЗ", IncomingName, "Incoming", 1,
                "Покупка ТМЗ у поставщика"),
            new KindDefinition("INCOMING_PRODUCTION", "Приход из производства", IncomingName, "Incoming", 2,
                "Возврат/поступление ТМЗ из производства"),
            new KindDefinition("INCOMING_ACT", "Приход по акту", IncomingName, "Incoming", 3,
                "Оприходование по акту (излишки, инвентаризация)"),
            new KindDefinition("OUTGOING_SALE", "Реализация ТМЗ", OutgoingName, "Outgoing", 4,
                "Продажа ТМЗ на сторону"),
            new KindDefinition("OUTGOING_PRODUCTION", "Расход в производство", OutgoingName, "Outgoing", 5,
                "Списание ТМЗ в производство"),
            new KindDefinition("OUTGOING_WRITEOFF", "Списание по акту", OutgoingName, "Outgoing", 6,
                "Списание по акту (недостача, порча)"),
            new KindDefinition("MOVEMENT_ASSIGNMENT", "Передача в подотчет", MovementName, "Movement", 7,
                "Передача ТМЗ материально ответственному лицу"),
            new KindDefinition("MOVEMENT_AUTO", "Автоматическая передача", MovementName, "Movement", 8,
                "Автоматическое перемещение между местами хранения")
        };

        /// <summary>Документы, которые удаляются: их роль перешла к виду операции.</summary>
        public static IReadOnlyList<string> ObsoleteDocumentNames { get; } = new[]
        {
            "Приход из производства ТМЦ",
            "Расход в производство",
            "Передача ТМЦ в подотчет",
            "Инвентаризация ТМЦ"
        };

        /// <summary>
        /// Поля шапки — одинаковый набор у всех трёх документов.
        /// Порядок повторяет окно эталона: документ → контрагент и реквизиты → налоги
        /// и валюта → итоги → служебные счета.
        /// </summary>
        public static List<MetadataField> HeaderFields(Guid metadataObjectId) => new()
        {
            Field(metadataObjectId, "Номер", "doc_number", "String", 1, required: true, length: 50),
            Field(metadataObjectId, "Дата", "doc_date", "DateTime", 2, required: true),
            Field(metadataObjectId, "Вид операции", "document_entry_id", "Reference", 3, required: true,
                reference: EntryCatalogName,
                displayPattern: "{Код} - {Наименование}", displayFields: "Код,Наименование"),
            Field(metadataObjectId, "Код вида операции", "operation_kind", "String", 4, length: 50),
            Field(metadataObjectId, "№ пачки", "pack_number", "Int", 5),
            Field(metadataObjectId, "Серия/№ бланка", "tax_blank_number", "String", 6, length: 50),
            Field(metadataObjectId, "Номер ЭСФ", "esf_number", "String", 7, length: 50),
            Field(metadataObjectId, "Счет", "counterparty_account", "Reference", 8,
                reference: ChartOfAccountsCatalogName,
                displayPattern: "{Код} - {Наименование}", displayFields: "Код,Наименование"),
            Field(metadataObjectId, "Организация", "organization_id", "Reference", 9, reference: "Организации"),
            Field(metadataObjectId, "Сотрудник", "employee_id", "Reference", 10,
                reference: "Сотрудники (Списочный состав)"),
            Field(metadataObjectId, "Вид покупки", "supply_kind", "Reference", 11, reference: "Типы поставки"),
            Field(metadataObjectId, "Вид НДС", "vat_type", "Reference", 12, reference: "Налоги"),
            Field(metadataObjectId, "Ставка НДС, %", "vat_rate", "Decimal", 13, precision: 5, scale: 2),
            Field(metadataObjectId, "Вид оплаты", "payment_kind", "Reference", 14, reference: "Виды оплаты"),
            Field(metadataObjectId, "Налог", "sales_tax_type", "Reference", 15, reference: "Налоги"),
            Field(metadataObjectId, "Ставка налога, %", "sales_tax_rate", "Decimal", 16, precision: 5, scale: 2),
            Field(metadataObjectId, "Валюта", "currency_id", "Reference", 17, reference: "Справочник валют"),
            Field(metadataObjectId, "Курс валюты", "exchange_rate", "Decimal", 18, precision: 18, scale: 4),
            Field(metadataObjectId, "Без налогов", "amount_without_tax", "Decimal", 19, precision: 18, scale: 2),
            Field(metadataObjectId, "Сумма НДС", "vat_total", "Decimal", 20, precision: 18, scale: 2),
            Field(metadataObjectId, "Сумма налога с продаж", "sales_tax_total", "Decimal", 21,
                precision: 18, scale: 2),
            Field(metadataObjectId, "Транспорт/расходы", "transport_amount", "Decimal", 22,
                precision: 18, scale: 2),
            Field(metadataObjectId, "Там. платежи", "customs_amount", "Decimal", 23, precision: 18, scale: 2),
            Field(metadataObjectId, "Прочие расходы", "other_expenses_amount", "Decimal", 24,
                precision: 18, scale: 2),
            Field(metadataObjectId, "Итого", "amount", "Decimal", 25, precision: 18, scale: 2),
            Field(metadataObjectId, "Сумма в валюте", "amount_currency", "Decimal", 26,
                precision: 18, scale: 2),
            Field(metadataObjectId, "Примечание", "basis", "String", 27, length: 500),
            Field(metadataObjectId, "Участок-получатель", "destination_site_id", "Reference", 28,
                reference: "Участки"),
            Field(metadataObjectId, "МОЛ-получатель", "destination_person_id", "Reference", 29, reference: "МОЛ"),
            Field(metadataObjectId, "Счет дебета", "debit_account", "Reference", 30,
                reference: ChartOfAccountsCatalogName,
                displayPattern: "{Код} - {Наименование}", displayFields: "Код,Наименование"),
            Field(metadataObjectId, "Счет кредита", "credit_account", "Reference", 31,
                reference: ChartOfAccountsCatalogName,
                displayPattern: "{Код} - {Наименование}", displayFields: "Код,Наименование"),
            Field(metadataObjectId, "Проведен", "is_posted", "Bool", 32, required: true)
        };

        /// <summary>
        /// Колонки строк (кроме Id, родителя и CreatedAt/UpdatedAt).
        /// Повторяют состав таблицы строк счёта-фактуры плюс «Дебет», «Участок», «МОЛ».
        /// </summary>
        public static IReadOnlyList<(string Column, string Definition)> LineColumns { get; } = new[]
        {
            ("line_number", "integer NOT NULL DEFAULT 0"),
            ("material_id", "uuid NULL"),
            ("material_name", "varchar(500) NULL"),
            ("unit_name", "varchar(50) NULL"),
            ("account_code", "varchar(50) NULL"),
            ("site_id", "uuid NULL"),
            ("responsible_person_id", "uuid NULL"),
            ("quantity", "numeric(18,3) NOT NULL DEFAULT 0"),
            ("price", "numeric(18,6) NOT NULL DEFAULT 0"),
            ("amount_without_tax", "numeric(18,2) NOT NULL DEFAULT 0"),
            ("vat_rate", "numeric(8,2) NOT NULL DEFAULT 0"),
            ("vat_amount", "numeric(18,2) NOT NULL DEFAULT 0"),
            ("sales_tax_rate", "numeric(8,2) NOT NULL DEFAULT 0"),
            ("sales_tax_amount", "numeric(18,2) NOT NULL DEFAULT 0"),
            ("line_total", "numeric(18,2) NOT NULL DEFAULT 0"),
            ("amount_currency", "numeric(18,2) NOT NULL DEFAULT 0"),
            ("description", "varchar(500) NULL")
        };

        /// <summary>Поля справочника «Ввод нового документа ТМЗ».</summary>
        public static List<MetadataField> EntryCatalogFields(Guid metadataObjectId) => new()
        {
            Field(metadataObjectId, "Код", "code", "String", 1, required: true, length: 50, unique: true),
            Field(metadataObjectId, "Наименование", "name", "String", 2, required: true, length: 200),
            Field(metadataObjectId, "Документ ТМЗ", "document_name", "String", 3, required: true, length: 120),
            Field(metadataObjectId, "Вид движения", "movement_kind", "String", 4, required: true, length: 40),
            Field(metadataObjectId, "Код операции", "operation_kind", "String", 5, length: 50),
            Field(metadataObjectId, "Порядок", "sort_order", "Int", 6),
            Field(metadataObjectId, "Описание", "description", "String", 7, length: 500),
            Field(metadataObjectId, "Активен", "is_active", "Bool", 8, required: true)
        };

        private static MetadataField Field(
            Guid metadataObjectId,
            string name,
            string column,
            string type,
            int order,
            bool required = false,
            bool unique = false,
            int length = 0,
            int precision = 18,
            int scale = 2,
            string? reference = null,
            string? displayPattern = null,
            string? displayFields = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            DbColumnName = column,
            FieldType = type,
            Order = order,
            IsRequired = required,
            IsUnique = unique,
            Length = length,
            Precision = precision,
            Scale = scale,
            ReferenceCatalog = reference,
            DisplayPattern = displayPattern,
            DisplayFields = displayFields,
            MetadataObjectId = metadataObjectId
        };
    }
}
