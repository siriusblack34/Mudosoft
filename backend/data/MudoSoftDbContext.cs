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
        public DbSet<CommandResultRecord> CommandResults => Set<CommandResultRecord>(); 

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 🏆 KRİTİK DÜZELTME 1: Devices.Id sütununun uzunluğunu kesinleştirme
            // Bu, Foreign Key'lerin de aynı uzunluğu (nvarchar(450)) kullanmasını sağlar.
            modelBuilder.Entity<Device>()
                .Property(d => d.Id)
                .HasMaxLength(450); 
            
            // 🏆 KRİTİK DÜZELTME 2: DeviceMetric ForeignKey uzunluğunu garantileme
           modelBuilder.Entity<DeviceMetric>()
        .HasOne(dm => dm.Device) // DeviceMetric modelinde 'Device' navigasyon özelliği olmalı
        .WithMany(d => d.Metrics)
        .HasForeignKey(dm => dm.DeviceId) // 'DeviceId' sütununu kullanmaya zorlar
        .IsRequired();
            
            // 🏆 KRİTİK DÜZELTME 3: CommandResultRecord ForeignKey uzunluğunu garantileme
            modelBuilder.Entity<CommandResultRecord>()
                .Property(cr => cr.DeviceId)
                .HasMaxLength(450);


            // CommandResultRecord için indeks ve kısıtlamalar (Mevcut mantık korunmuştur)
            modelBuilder.Entity<CommandResultRecord>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => r.DeviceId);
                e.HasIndex(r => r.CommandId).IsUnique(); 
            });
            
            // DeviceMetric'ler için de Foreign Key'i (DeviceId) yapılandırın. 
            // Bu, AddCurrentMetricsToDevice migration'ının doğru çalışması için önemlidir.
            modelBuilder.Entity<DeviceMetric>()
                .HasOne<Device>()
                .WithMany(d => d.Metrics)
                .HasForeignKey(dm => dm.DeviceId);
        }
    }
}