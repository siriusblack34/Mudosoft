using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using MudoSoft.Backend.Data;

namespace MudoSoft.Backend;

/// <summary>
/// Entity Framework Core Tasarım Zamanı Fabrikası.
/// dotnet ef komutlarının (migrations, database update) DbContext'i doğru şekilde bulması ve oluşturması için gereklidir.
/// </summary>
public class MudoSoftDbContextFactory : IDesignTimeDbContextFactory<MudoSoftDbContext>
{
    public MudoSoftDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MudoSoftDbContext>();
        
        // EF Core'un veritabanı tipini bilmesi gerekir.
        // Buradaki ConnectionString sadece migrasyon oluşturma zamanında kullanılır.
        // Gerçek bağlantı hala appsettings.json dosyanızdan alınacaktır.
        // Kendi ConnectionString'inize yakın bir tip belirleyin:
        optionsBuilder.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=Design_Time_MudoSoft;Trusted_Connection=True;MultipleActiveResultSets=true");

        return new MudoSoftDbContext(optionsBuilder.Options);
    }
}