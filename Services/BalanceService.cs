using BIS.ERP.Data;
using BIS.ERP.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace BIS.ERP.Services
{
    public class BalanceService
    {
        private readonly AppDbContext _context;

        public BalanceService(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Получение оборотного баланса за период
        /// </summary>
        public async Task<List<TurnoverBalance>> GetTurnoverBalanceAsync(DateTime startDate, DateTime endDate)
        {
            // SQL запрос для получения оборотов по счетам
            var sql = @"
                WITH turnovers AS (
                    SELECT 
                        account_debit AS account_code,
                        SUM(amount) AS debit_turnover,
                        0 AS credit_turnover
                    FROM postings
                    WHERE posting_date BETWEEN @startDate AND @endDate
                    GROUP BY account_debit
                    
                    UNION ALL
                    
                    SELECT 
                        account_credit AS account_code,
                        0 AS debit_turnover,
                        SUM(amount) AS credit_turnover
                    FROM postings
                    WHERE posting_date BETWEEN @startDate AND @endDate
                    GROUP BY account_credit
                ),
                aggregated AS (
                    SELECT 
                        account_code,
                        SUM(debit_turnover) AS total_debit,
                        SUM(credit_turnover) AS total_credit
                    FROM turnovers
                    GROUP BY account_code
                )
                SELECT 
                    a.account_code,
                    pc.name AS account_name,
                    COALESCE(a.total_debit, 0) AS turnover_debit,
                    COALESCE(a.total_credit, 0) AS turnover_credit
                FROM aggregated a
                LEFT JOIN plan_accounts pc ON a.account_code = pc.code
                ORDER BY a.account_code
            ";

            var result = new List<TurnoverBalance>();

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 60;

            // Параметры
            var startParam = command.CreateParameter();
            startParam.ParameterName = "@startDate";
            startParam.Value = startDate;
            command.Parameters.Add(startParam);

            var endParam = command.CreateParameter();
            endParam.ParameterName = "@endDate";
            endParam.Value = endDate;
            command.Parameters.Add(endParam);

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var code = reader.GetString(0);
                    var name = reader.IsDBNull(1) ? code : reader.GetString(1);
                    var debit = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);
                    var credit = reader.IsDBNull(3) ? 0 : reader.GetDecimal(3);

                    result.Add(new TurnoverBalance
                    {
                        AccountCode = code,
                        AccountName = name,
                        TurnoverDebit = debit,
                        TurnoverCredit = credit
                    });
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }

        /// <summary>
        /// Получение Главной книги
        /// </summary>
        public async Task<List<GeneralLedger>> GetGeneralLedgerAsync(int year)
        {
            // Сложный запрос для главной книги
            // ...
            return new List<GeneralLedger>();
        }

        /// <summary>
        /// Получение баланса предприятия
        /// </summary>
        public async Task<EnterpriseBalance> GetEnterpriseBalanceAsync(DateTime date)
        {
            var balance = new EnterpriseBalance();

            // Получаем остатки по активным счетам
            var assets = await GetBalanceByAccountTypeAsync("ASSET", date);
            balance.Assets = assets;
            balance.TotalAssets = assets.Sum(a => a.Amount);

            // Получаем остатки по пассивным счетам
            var liabilities = await GetBalanceByAccountTypeAsync("LIABILITY", date);
            balance.Liabilities = liabilities;
            balance.TotalLiabilities = liabilities.Sum(l => l.Amount);

            return balance;
        }

        private async Task<List<BalanceItem>> GetBalanceByAccountTypeAsync(string type, DateTime date)
        {
            var sql = @"
                SELECT 
                    pc.code,
                    pc.name,
                    COALESCE((
                        SELECT SUM(amount)
                        FROM postings
                        WHERE account_debit = pc.code
                        AND posting_date <= @date
                    ), 0) - COALESCE((
                        SELECT SUM(amount)
                        FROM postings
                        WHERE account_credit = pc.code
                        AND posting_date <= @date
                    ), 0) AS balance
                FROM plan_accounts pc
                WHERE pc.account_type = @type
                ORDER BY pc.code
            ";

            var result = new List<BalanceItem>();

            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;

            var dateParam = command.CreateParameter();
            dateParam.ParameterName = "@date";
            dateParam.Value = date;
            command.Parameters.Add(dateParam);

            var typeParam = command.CreateParameter();
            typeParam.ParameterName = "@type";
            typeParam.Value = type;
            command.Parameters.Add(typeParam);

            try
            {
                await _context.Database.OpenConnectionAsync();
                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    var code = reader.GetString(0);
                    var name = reader.IsDBNull(1) ? code : reader.GetString(1);
                    var amount = reader.IsDBNull(2) ? 0 : reader.GetDecimal(2);

                    result.Add(new BalanceItem
                    {
                        AccountCode = code,
                        AccountName = name,
                        Amount = amount
                    });
                }
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }

            return result;
        }
    }
}