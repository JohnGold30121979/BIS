# BIS Patch

`*.bispatch` - зашифрованный пакет обновления инфобазы.

Пакет создается из папки с файлами и шифруется через `BisPackageCryptoService`.
Внутри пакета используется zip-архив со следующими файлами:

- `manifest.json` - обязательный паспорт патча.
- `schema.sql` - необязательные SQL-изменения структуры.
- `metadata.json` - необязательный список `MetadataObject`.
- `reports.json` - необязательный список `Report`.
- `modules.json` - необязательные модули и привязки объектов к модулям.
- `data.json` - необязательные строки таблиц в режиме `Upsert` или `Replace`.

Минимальный `manifest.json`:

```json
{
  "Format": "BIS.Patch",
  "FormatVersion": 1,
  "PatchId": "2026-07-001",
  "Version": "1.0.0",
  "Name": "Первый патч инфобазы",
  "Description": "Описание изменений",
  "Author": "BIS",
  "MinAppVersion": "",
  "CreatedAt": "2026-07-01T00:00:00Z",
  "Dependencies": []
}
```

После применения запись попадает в таблицу `system_patches`.
Повторное применение патча с тем же `PatchId` блокируется.
