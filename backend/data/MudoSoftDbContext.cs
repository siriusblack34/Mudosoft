// backend/data/MudoSoftDbContext.cs (KRİTİK HATA DÜZELTİLMİŞTİR)

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
        
        // FIX: CommandResultRecords DbSet'i standart olarak tutuldu. (CommandResults kaldırıldı)
        public DbSet<CommandResultRecord> CommandResultRecords { get; set; } 
        
        // FIX: ActionRecords DbSet'i eklendi (CS1061 hatalarını giderir)
        public DbSet<ActionRecord> ActionRecords { get; set; }
        
        // YENİ: StoreDevice DbSet'i
        public DbSet<StoreDevice> StoreDevices { get; set; }
        public DbSet<CommandResultRecord> CommandResults { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            
            modelBuilder.Entity<Device>()
                .Property(d => d.Id)
                .HasMaxLength(450); 
            
            modelBuilder.Entity<DeviceMetric>()
                .HasOne(dm => dm.Device)
                .WithMany(d => d.Metrics)
                .HasForeignKey(dm => dm.DeviceId) 
                .IsRequired();
            
            modelBuilder.Entity<CommandResultRecord>()
                .Property(cr => cr.DeviceId)
                .HasMaxLength(450);

            modelBuilder.Entity<StoreDevice>()
                .HasKey(sd => sd.DeviceId);
            
            modelBuilder.Entity<StoreDevice>()
                .HasIndex(sd => new { sd.StoreCode, sd.DeviceType })
                .IsUnique();

            modelBuilder.Entity<CommandResultRecord>(e =>
            {
                e.HasKey(r => r.Id);
                e.HasIndex(r => r.DeviceId);
                e.HasIndex(r => r.CommandId).IsUnique(); 
            });
            
            modelBuilder.Entity<DeviceMetric>()
                .HasOne<Device>()
                .WithMany(d => d.Metrics)
                .HasForeignKey(dm => dm.DeviceId);

            
        }
    }
}