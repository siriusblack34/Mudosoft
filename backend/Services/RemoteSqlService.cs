using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;

namespace MudoSoft.Backend.Services
{
    public class RemoteSqlService : IRemoteSqlService
    {
        public async Task<bool> TestSqlConnectionAsync(string connectionString)
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DataTable?> ExecuteQueryAsync(string connectionString, string query)
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                using var cmd = new SqlCommand(query, conn);

                cmd.CommandTimeout = 5;
                await conn.OpenAsync();

                using var reader = await cmd.ExecuteReaderAsync();
                var dt = new DataTable();
                dt.Load(reader);
                return dt;
            }
            catch
            {
                return null;
            }
        }

        public async Task<string> ExecuteQueryAndReturnJsonAsync(string connectionString, string sqlQuery)
        {
            // SELECT dışı sorguları engelle
            if (!sqlQuery.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase))
                return "[]";

            var table = await ExecuteQueryAsync(connectionString, sqlQuery);

            if (table == null)
                return "[]";

            // DataTable → List<Dictionary<string,object>>
            var rows = new List<Dictionary<string, object?>>();
            foreach (DataRow row in table.Rows)
            {
                var item = new Dictionary<string, object?>();

                foreach (DataColumn col in table.Columns)
                {
                    item[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                }

                rows.Add(item);
            }

            return JsonSerializer.Serialize(rows);
        }
    }
}
