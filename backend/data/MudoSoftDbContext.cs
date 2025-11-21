using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Models;

namespace MudoSoft.Backend.Data
{
    public class MudoSoftDbContext : DbContext
    {
        public MudoSoftDbContext(DbContextOptions<MudoSoftDbContext> options)
            : base(options)
        {
        }

        public DbSet<Device> Devices => Set<Device>();
        public DbSet<DeviceMetric> DeviceMetrics => Set<DeviceMetric>();
        public DbSet<CommandResultRecord> CommandResults => Set<CommandResultRecord>(); // Yeni: Komut Geçmişi
        // ...
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ... Mevcut Device ve DeviceMetric konfigürasyonları ...
            
            modelBuilder.Entity<CommandResultRecord>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => r.DeviceId); // Cihaza göre hızlı sorgu için indeks
                e.HasIndex(r => r.CommandId).IsUnique(); // CommandId'nin tekil olmasını sağlar
            });
        }
    }
}