using System.Data;
using System.Threading.Tasks;

namespace Orchestra.Backend.Services
{
    public interface IRemoteSqlService
    {
        Task<string> ExecuteQueryAndReturnJsonAsync(string connectionString, string sqlQuery);
        Task<DataTable?> ExecuteQueryAsync(string connectionString, string query);
        Task<bool> TestSqlConnectionAsync(string connectionString);
    }
}
