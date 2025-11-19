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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Device>(e =>
            {
                e.HasKey(d => d.Id);
                e.Property(d => d.Id).HasMaxLength(100);
                e.Property(d => d.IpAddress).HasMaxLength(50);
                e.Property(d => d.StoreName).HasMaxLength(200);
            });

            modelBuilder.Entity<DeviceMetric>(e =>
            {
                e.HasKey(m => m.Id);
                e.HasIndex(m => new { m.DeviceId, m.TimestampUtc });
            });
        }
    }
}
