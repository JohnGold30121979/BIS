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
    private readonly AppDbContext _masterContext;
    private InfoBase? _currentInfoBase;

    public InfoBaseManager()
    {
        _masterContext = new AppDbContext();
        EnsureMasterDatabaseExists();
    }

    private void EnsureMasterDatabaseExists()
    {
        try
        {
            _masterContext.Database.EnsureCreated();
        }
        catch (NpgsqlException)
        {
            var settings = AppSettings.Instance;
            using var connection = new NpgsqlConnection($"Host={settings.Host};Port={settings.Port};Username={settings.Username};Password={settings.Password}");
            connection.Open();

            using var cmd = new NpgsqlCommand($"CREATE DATABASE {settings.DatabaseName}", connection);
            cmd.ExecuteNonQuery();

            _masterContext.Database.EnsureCreated();
        }
    }

    public async Task<List<InfoBase>> GetInfoBasesAsync()
    {
        return await _masterContext.InfoBases.ToListAsync();
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
        return await CreateInfoBaseAsync(name, type,
            AppSettings.Instance.Host,
            AppSettings.Instance.Port,
            AppSettings.Instance.Username,
            AppSettings.Instance.Password);
    }

    public async Task<InfoBase> CreateInfoBaseAsync(string name, string type,
        string host, int port, string username, string password)
    {
        var dbName = $"bis_{name.ToLower().Replace(" ", "_")}_{Guid.NewGuid():N}";

        // Создаем физическую БД
        using var connection = new NpgsqlConnection($"Host={host};Port={port};Username={username};Password={password}");
        await connection.OpenAsync();

        using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", connection);
        await cmd.ExecuteNonQueryAsync();

        // Создаем структуру БД
        var connectionString = AppDbContext.BuildConnectionString(host, port, dbName, username, password);
        var dbContext = new AppDbContext(connectionString);
        await dbContext.Database.EnsureCreatedAsync();

        // Создаем запись в мастер-БД
        var infoBase = new InfoBase
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = type,
            Host = host,
            Port = port,
            DatabaseName = dbName,
            Username = username,
            Password = password,
            CreatedAt = DateTime.Now,
            IsActive = false,
            Version = "1.0"
        };

        _masterContext.InfoBases.Add(infoBase);
        await _masterContext.SaveChangesAsync();

        return infoBase;
    }

    public async Task<bool> DeleteInfoBaseAsync(Guid id)
    {
        var infoBase = await _masterContext.InfoBases.FindAsync(id);
        if (infoBase == null)
            return false;

        try
        {
            using var connection = new NpgsqlConnection($"Host={infoBase.Host};Port={infoBase.Port};Username={infoBase.Username};Password={infoBase.Password}");
            await connection.OpenAsync();

            using var killCmd = new NpgsqlCommand($@"
                SELECT pg_terminate_backend(pg_stat_activity.pid)
                FROM pg_stat_activity
                WHERE pg_stat_activity.datname = '{infoBase.DatabaseName}'
                  AND pid <> pg_backend_pid()", connection);
            await killCmd.ExecuteNonQueryAsync();

            using var dropCmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{infoBase.DatabaseName}\"", connection);
            await dropCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error deleting database: {ex.Message}");
        }

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