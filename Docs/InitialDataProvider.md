# InitialDataProvider — начальные данные справочников

Дата: 19.10.2026. Ветка `main`.
Файл: `Services/InitialDataProvider.cs` (735 строк, `public static class`, без EF/PostgreSQL, без SQL).

## 1. Что это и зачем

Чистая фабрика данных — **«ЧТО сеем»**. Не знает про БД, `DbContext`, SQL, `Npgsql`.
Возвращает `List<T>` / `IReadOnlyList<T>` DTO из `BIS.ERP.Models` (`ChartOfAccount`, `Bank`, ...) и вложенных `sealed record` (`SeedCurrency`, `SeedCountry`, ...).
Fox-специфика (8-значные коды `XXXXYYYY`, `SCHET|NAIM|...`) изолирована здесь.

Похожий `Services/MetadataService.DataSeed.cs` (partial `MetadataService`, ~1509 строк) — это «КАК сеем»: `Add*DataToTable(catalog)` / `Ensure*DataAsync(catalog)` пишут в `catalog_*` через `_context`, `UpsertCatalogSeedRowAsync`, raw SQL. Разделение: данные отдельно, запись в БД отдельно.

## 2. Состав (публичные методы)

| Метод | Что отдает |
|---|---|
| `GetChartOfAccounts()` :11 | делегирует в `GetFoxChartOfAccounts()` |
| `GetFoxChartOfAccounts()` :16 | парсит `FoxChartOfAccountsData` :71 (`Split('\n')` → `Split('|')`, пропуск строк с `<16` полей) |
| `GetFallbackChartOfAccounts()` | захардкоженные `Code="11100000"...` по разделам 2000–9000 + забаланс (только `Code/Name/AccountType/Level`) |
| `GetBanks()` :430 | ~20 банков КР (`Code/Name/BIC/Branch/Address/Phone/Swift/Chips/AddressEng/IsActive`) |
| `GetBankAccounts()` / `GetSupplyKinds()` / `GetVatRates()` / `GetPaymentKinds()` / `GetPaymentClassifications()` | счета, виды поставок, НДС, виды/классификация платежей |

## 3. Формат Fox-строк :71

16 полей, разделитель `|`:

```
Code|Name|Description|AccountType|Level|Closing|Print|Balance|UseOrg|UseEmp|UseMat|UseCurr|UsePers|UseSites|Tax|Analytics
11000000|КАССА ЦЕНТРАЛЬНАЯ||Active|1|3|6|6|false|false|false|false|false|false|0|0
```

Маппинг: `Code=[0], Name=[1], Description=[2], AccountType=[3], Level=[4] def 1, ClosingSubsystemCode=[5], PrintModeCode=[6], BalanceModeCode=[7], UseOrganizations=[8], UseEmployees=[9], UseMaterials=[10], UseCurrencies=[11], UsePersonalAccounts=[12], UseSites=[13], TaxCode=[14], AnalyticsGroupCode=[15]`. `IsActive=true` всегда. Парсинг терпимый: `ReadChartInt :54 (InvariantCulture)`, `ReadChartBool :63 (bool.TryParse)`.

## 4. Когда вызывается

Только из сидов `MetadataService`, из UI напрямую — никогда:

```
Старт/синхронизация инфобазы
 └─ MetadataService.Catalogs.cs: EnsureChartOfAccountsCatalogStructureAsync() :295
     └─ SynchronizeChartOfAccountsSourceDataAsync() :1847  ← единственный потребитель плана
         ├─ var accounts = InitialDataProvider.GetChartOfAccounts();
         ├─ existingCodes = GetCatalogCodeSetAsync(table)
         ├─ foreach: UpsertCatalogSeedRowAsync(table, code, BuildChartOfAccountsCatalogValues(account)) :2035
         └─ DeactivateLegacyFallbackChartAccountsAsync() :1993 — гасит fallback-коды без Fox-аналога (is_active=false, не DELETE)
Банки/счета/поставки: CreateBankCatalog()/EnsureBankCatalogDataAsync() → GetBanks()/GetBankAccounts() → DataSeed.AddBankDataToTable()/UpsertCatalogSeedRowAsync().
```

Валюты (`KGS/USD/...`), материалы (`1–6`), должности, `Кассы`, первичная организация — захардкожены прямо в `MetadataService.DataSeed.cs` (`AddCurrencyDataToTable :22`, `AddCashDeskDataToTable`, `AddPrimaryOrganizationDataToTable :695`). Fox-наследие вынесли в провайдер, мелкие справочники оставили инлайн.

## 5. Почему два похожих модуля

|  | `InitialDataProvider.cs` | `MetadataService.DataSeed.cs` |
|---|---|---|
| Тип | `static class`, pure | `partial MetadataService`, instance |
| Зависимости | только `BIS.ERP.Models` | `_context`, Npgsql, `information_schema` |
| Идемпотентность | нет (`new List`) | да (`Upsert WHERE NOT EXISTS`, `Ensure COUNT==0`) |
| Тест | unit без БД | только интеграционно |

Правило: новый справочник = `GetXxx()` в провайдере + `AddXxxDataToTable()/EnsureXxxDataAsync()` в `DataSeed` + `CreateXxxCatalog()` в `Catalogs.cs`. SQL в провайдере не писать, Fox-строки в сиде не хардкодить.

## 6. Связанные файлы

- `Services/MetadataService.Catalogs.cs:259` создание `План счетов`; `:1847` синхронизация; `:2035` маппинг.
- `Services/ChartOfAccountsDbfImportService.cs:15` `SCHET→Код`.
- `Services/ChartOfAccountsSelectionMetadata.cs` `NormalizeModuleKey`.
- `Docs/План_нормализации_модели_данных.md §3.6` переименование `catalog_plan_schetov_20260728054755 → catalog_plan_schetov`.


