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

        public async Task<DataTable> ExecuteQueryAsync(string connectionString, string query)
        {
            // try-catch bloğunu kaldırdık ki hata Controller'a fırlatılsın ve kullanıcı görsün.
            using var conn = new SqlConnection(connectionString);
            using var cmd = new SqlCommand(query, conn);

            cmd.CommandTimeout = 30; // 5sn çok kısaydı, 30sn yaptık.
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();
            var dt = new DataTable();
            dt.Load(reader);
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
            
            var table = await ExecuteQueryAsync(connectionString, sqlQuery);

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
