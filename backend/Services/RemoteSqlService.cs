using System;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Orchestra.Backend.Services
{
    public class RemoteSqlService : IRemoteSqlService
    {
        public async Task<bool> TestSqlConnectionAsync(string connectionString)
        {
            try
            {
                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<DataTable?> ExecuteQueryAsync(string connectionString, string query)
        {
            using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            var dt = new DataTable();
            
            // GO komutuna göre script'i parçalara ayır
            var statements = Regex.Split(query, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

            foreach (var statement in statements)
            {
                var sqlPart = statement.Trim();
                if (string.IsNullOrWhiteSpace(sqlPart)) continue;

                using var cmd = new SqlCommand(sqlPart, conn);
                cmd.CommandTimeout = 30;
                
                using var reader = await cmd.ExecuteReaderAsync();
                
                // Eğer SELECT sorgusuysa ve veriler varsa son tabloyu yükler
                if (reader.FieldCount > 0)
                {
                    dt.Clear();
                    dt.Load(reader);
                }
            }
            
            return dt;
        }

        public async Task<string> ExecuteQueryAndReturnJsonAsync(string connectionString, string sqlQuery)
        {
            // 'Only SELECT' engelini kaldırdık. Artık UPDATE/DELETE de çalışır.
            
            // Eğer UPDATE/INSERT/DELETE ise DataTable boş dönebilir ama Exception fırlatmazsa başarılıdır.
            // Bunu ayırt etmek için basit bir kontrol ekleyebiliriz veya 
            // ExecuteQueryAsync içinde reader.RecordsAffected kontrol edilebilir 
            // ama şimdilik DataTable mantığı ile devam edelim.
            // DataTable boş dönerse [] döner, bu da front-end için 'başarılı ama veri yok' demektir.
            
            // Ancak asıl sorun: Hata olduğunda null dönüyor ve [] gidiyordu.
            // Artık hata fırlatılacak.
            
            using var table = await ExecuteQueryAsync(connectionString, sqlQuery);
            if (table == null) return "[]";

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

            // Eğer sorgu bir UPDATE/DELETE ise ve satır dönmediyse (ama hata da almadıysak)
            // Kullanıcıya bir şey göstermek iyi olur.
            // Fakat DataTable reader'dan şema alamazsa column count 0 olur.
            if (rows.Count == 0 && !sqlQuery.TrimStart().StartsWith("select", StringComparison.OrdinalIgnoreCase))
            {
                // Bir "Affected Rows" bilgisi dönmek daha şık olurdu ama 
                // şimdilik en azından hata vermediğini biliyoruz.
                // Boş array dönersek "0 rows" yazar, bu da teknik olarak doğru.
            }

            return JsonSerializer.Serialize(rows);
        }
    }
}
