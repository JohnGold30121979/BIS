using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using BIS.ERP.Models;
using BIS.ERP.Data;
using BIS.ERP.Services;

namespace BIS.ERP.Services;

public class InfoBaseManager
{
    public const string DefaultPatchVersion = "1.0.0";

    private readonly AppDbContext _masterContext;
    private InfoBase? _currentInfoBase;

    public InfoBaseManager()
    {
        _masterContext = new AppDbContext();
        EnsureMasterDatabaseExists();
        EnsureMasterSchema();
    }

    private void EnsureMasterDatabaseExists()
    {
        try
        {
            // Пытаемся использовать мастер-базу
            _masterContext.Database.EnsureCreated();
        }
        catch (NpgsqlException ex) when (ex.SqlState == "3D000") // База не существует
        {
            // Подключаемся к системной базе postgres и создаем bis_master
            var settings = AppSettings.Instance;
            using var connection = new NpgsqlConnection(settings.GetPostgresConnectionString());
            connection.Open();

            var quotedDatabaseName = new NpgsqlCommandBuilder().QuoteIdentifier(settings.DatabaseName);
            using var cmd = new NpgsqlCommand($"CREATE DATABASE {quotedDatabaseName}", connection);
            cmd.ExecuteNonQuery();

            // Теперь создаем таблицы в новой базе
            _masterContext.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in EnsureMasterDatabaseExists: {ex.Message}");
            throw;
        }
    }

    private void EnsureMasterSchema()
    {
        _masterContext.Database.ExecuteSqlRaw(@"
            ALTER TABLE ""InfoBases"" ADD COLUMN IF NOT EXISTS ""Version"" varchar(50);
            ALTER TABLE ""InfoBases"" ALTER COLUMN ""Version"" TYPE varchar(50);
            UPDATE ""InfoBases"" SET ""Version"" = @version WHERE ""Version"" IS NULL OR ""Version"" = '';",
            new NpgsqlParameter("@version", DefaultPatchVersion));
    }

    public async Task<List<InfoBase>> GetInfoBasesAsync()
    {
        var bases = await _masterContext.InfoBases.OrderBy(item => item.Name).ToListAsync();
        foreach (var infoBase in bases)
            await RefreshInfoBasePatchVersionAsync(infoBase, createBaselineWhenMissing: true);
        return bases;
    }

    public async Task<InfoBase?> GetCurrentInfoBaseAsync()
    {
        if (_currentInfoBase != null)
            return _currentInfoBase;

        _currentInfoBase = await _masterContext.InfoBases.FirstOrDefaultAsync(x => x.IsActive);
        return _currentInfoBase;
    }

    public async Task<InfoBase> CreateInfoBaseAsync(string name, string type)
    {
        var settings = AppSettings.Instance;
        return await CreateInfoBaseAsync(name, type,
            settings.Host,
            settings.Port,
            settings.Username,
            settings.Password,
            patchVersion: DefaultPatchVersion);
    }

    public async Task<InfoBase> CreateInfoBaseAsync(string name, string type,
    string host, int port, string username, string password, string? databaseName = null,
    string? patchVersion = null)
    {
        var dbName = string.IsNullOrWhiteSpace(databaseName) ? $"bis_{Guid.NewGuid():N}" : databaseName.Trim();
        var normalizedPatchVersion = NormalizePatchVersion(patchVersion);
        if (!System.Text.RegularExpressions.Regex.IsMatch(dbName, "^[a-zA-Z0-9_]+$"))
            throw new ArgumentException("Имя базы данных может содержать только латинские буквы, цифры и знак подчеркивания.");

        try
        {
            using var connection = new NpgsqlConnection(
                $"Host={host};Port={port};Database=postgres;Username={username};Password={password}");
            await connection.OpenAsync();

            using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
            await cmd.ExecuteNonQueryAsync();

            var connectionString = AppDbContext.BuildConnectionString(host, port, dbName, username, password);
            using var dbContext = new AppDbContext(connectionString);
            await dbContext.Database.EnsureCreatedAsync();
            await new RuntimeSchemaFixService(dbContext).EnsureAsync();

            var infoBase = new InfoBase
            {
                Id = Guid.NewGuid(),
                Name = name,
                Type = "Universal",  // Всегда Universal
                Description = name,
                Host = host,
                Port = port,
                DatabaseName = dbName,
                Username = username,
                Password = password,
                CreatedAt = DateTime.UtcNow,
                IsActive = false,
                Version = normalizedPatchVersion
            };

            // В InfoBaseManager.CreateInfoBaseAsync
            var metadataService = new MetadataService(dbContext);
            await metadataService.InitializeDefaultMetadataAsync(Guid.Empty);
            await metadataService.InitializePredefinedCatalogsAsync(infoBase.Id); // ← только здесь
            await new DocumentationMetadataSeedService(dbContext).EnsureAsync();
            await new PrintFormService(dbContext).SeedCashOrderFormsAsync();
            await new BisPatchService(dbContext).EnsureBaselinePatchAsync(normalizedPatchVersion);

            _masterContext.InfoBases.Add(infoBase);
            await _masterContext.SaveChangesAsync();

            return infoBase;
        }
        catch (Exception ex)
        {
            throw new Exception($"Ошибка создания базы данных: {ex.Message}");
        }
    }

    public async Task<bool> DeleteInfoBaseAsync(Guid id)
    {
        var infoBase = await _masterContext.InfoBases.FindAsync(id);
        if (infoBase == null)
            return false;

        try
        {
            // Подключаемся к системной базе postgres (более надежно)
            using var connection = new NpgsqlConnection($"Host={infoBase.Host};Port={infoBase.Port};Database=postgres;Username={infoBase.Username};Password={infoBase.Password}");
            await connection.OpenAsync();

            // Закрываем все подключения к удаляемой базе
            using var killCmd = new NpgsqlCommand($@"
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = '{infoBase.DatabaseName}'", connection);
            int killed = await killCmd.ExecuteNonQueryAsync();
            System.Diagnostics.Debug.WriteLine($"Закрыто подключений: {killed}");

            // Небольшая пауза для завершения процессов
            await Task.Delay(500);

            // Удаляем базу данных
            using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{infoBase.DatabaseName}\"", connection);
            await dropCmd.ExecuteNonQueryAsync();

            System.Diagnostics.Debug.WriteLine($"База данных '{infoBase.DatabaseName}' успешно удалена");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка при удалении базы: {ex.Message}");
            // Пробуем альтернативный способ - через мастер-базу
            try
            {
                using var connection = new NpgsqlConnection(AppSettings.Instance.GetMasterConnectionString());
                await connection.OpenAsync();

                using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{infoBase.DatabaseName}\"", connection);
                await dropCmd.ExecuteNonQueryAsync();
                System.Diagnostics.Debug.WriteLine($"База данных '{infoBase.DatabaseName}' удалена (альтернативный способ)");
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при альтернативном удалении: {ex2.Message}");
            }
        }

        // Удаляем запись из мастер-базы
        _masterContext.InfoBases.Remove(infoBase);
        await _masterContext.SaveChangesAsync();

        if (_currentInfoBase?.Id == id)
            _currentInfoBase = null;

        return true;
    }

    public async Task<bool> SetCurrentInfoBaseAsync(Guid id)
    {
        var allBases = await _masterContext.InfoBases.ToListAsync();
        foreach (var ib in allBases)
        {
            ib.IsActive = false;
        }

        var selected = await _masterContext.InfoBases.FindAsync(id);
        if (selected == null)
            return false;

        selected.IsActive = true;
        await _masterContext.SaveChangesAsync();

        _currentInfoBase = selected;
        return true;
    }

    public async Task<InfoBase> AttachInfoBaseAsync(
        string name, string host, int port, string databaseName, string username, string password,
        string? patchVersion = null)
    {
        if (!await TestConnectionAsync(host, port, databaseName, username, password))
            throw new InvalidOperationException("Не удалось подключиться к указанной базе данных.");
        if (await _masterContext.InfoBases.AnyAsync(item =>
                item.Host == host && item.Port == port && item.DatabaseName == databaseName))
            throw new InvalidOperationException("Эта база данных уже добавлена в список.");

        var connectionString = AppDbContext.BuildConnectionString(host, port, databaseName, username, password);
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(@"
                SELECT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = 'MetadataObjects')", connection);
            var isBisDatabase = Convert.ToBoolean(await command.ExecuteScalarAsync());
            if (!isBisDatabase)
                throw new InvalidOperationException(
                    "Указанная база доступна, но не содержит структуру BIS ERP. Создайте новую базу или выберите существующую базу BIS ERP.");
        }

        var normalizedPatchVersion = NormalizePatchVersion(patchVersion);
        await using (var dbContext = new AppDbContext(connectionString))
        {
            var patchService = new BisPatchService(dbContext);
            var currentPatchVersion = await patchService.GetCurrentPatchVersionAsync();
            if (string.IsNullOrWhiteSpace(currentPatchVersion))
            {
                await patchService.EnsureBaselinePatchAsync(normalizedPatchVersion);
                currentPatchVersion = normalizedPatchVersion;
            }
            normalizedPatchVersion = currentPatchVersion;
        }

        var infoBase = new InfoBase
        {
            Name = name.Trim(), Description = name.Trim(), Type = "Universal",
            Host = host.Trim(), Port = port, DatabaseName = databaseName.Trim(),
            Username = username.Trim(), Password = password, IsActive = false,
            Version = normalizedPatchVersion, CreatedAt = DateTime.UtcNow
        };
        await _masterContext.InfoBases.AddAsync(infoBase);
        await _masterContext.SaveChangesAsync();
        return infoBase;
    }

    public async Task UpdateInfoBaseNameAsync(Guid id, string name)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
            throw new ArgumentException("Наименование информационной базы не может быть пустым.");
        if (await _masterContext.InfoBases.AnyAsync(item => item.Id != id && item.Name == normalizedName))
            throw new InvalidOperationException("Информационная база с таким наименованием уже существует.");

        var infoBase = await _masterContext.InfoBases.FindAsync(id)
            ?? throw new InvalidOperationException("Информационная база не найдена.");
        infoBase.Name = normalizedName;
        infoBase.Description = normalizedName;
        await _masterContext.SaveChangesAsync();
        if (_currentInfoBase?.Id == id)
            _currentInfoBase = infoBase;
    }

    public async Task RefreshCurrentInfoBaseVersionAsync()
    {
        var current = await GetCurrentInfoBaseAsync();
        if (current != null)
            await RefreshInfoBasePatchVersionAsync(current, createBaselineWhenMissing: true);
    }

    private async Task RefreshInfoBasePatchVersionAsync(
        InfoBase infoBase,
        bool createBaselineWhenMissing)
    {
        try
        {
            await using var dbContext = new AppDbContext(infoBase.ConnectionString);
            var patchService = new BisPatchService(dbContext);
            var version = await patchService.GetCurrentPatchVersionAsync();
            if (string.IsNullOrWhiteSpace(version) && createBaselineWhenMissing)
            {
                version = NormalizePatchVersion(infoBase.Version);
                await patchService.EnsureBaselinePatchAsync(version);
            }

            if (!string.IsNullOrWhiteSpace(version) &&
                !string.Equals(infoBase.Version, version, StringComparison.OrdinalIgnoreCase))
            {
                infoBase.Version = version;
                await _masterContext.SaveChangesAsync();
                if (_currentInfoBase?.Id == infoBase.Id)
                    _currentInfoBase.Version = version;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Не удалось обновить версию патча инфобазы {infoBase.Name}: {ex.Message}");
        }
    }

    private static string NormalizePatchVersion(string? version)
    {
        var normalized = string.IsNullOrWhiteSpace(version) ? DefaultPatchVersion : version.Trim();
        return normalized.Length > 50 ? normalized[..50] : normalized;
    }

    public async Task<AppDbContext> GetCurrentDbContextAsync()
    {
        var current = await GetCurrentInfoBaseAsync();
        if (current == null)
            throw new Exception("Не выбрана информационная база");

        return new AppDbContext(current.ConnectionString);
    }

    public async Task InitializeDefaultBasesAsync()
    {
        var existing = await _masterContext.InfoBases.AnyAsync();
        if (existing)
            return;

        var defaultBases = new[]
        {
            new { Name = "Основная финансовая", Type = "Finance" },
            new { Name = "Складской учет", Type = "Inventory" },
            new { Name = "Кадры и зарплата", Type = "Salary" }
        };

        foreach (var def in defaultBases)
        {
            await CreateInfoBaseAsync(def.Name, def.Type);
        }

        var first = await _masterContext.InfoBases.FirstOrDefaultAsync();
        if (first != null)
            await SetCurrentInfoBaseAsync(first.Id);
    }

    public async Task<bool> TestConnectionAsync(string host, int port, string database, string username, string password)
    {
        try
        {
            var connectionString = AppDbContext.BuildConnectionString(host, port, database, username, password);
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
