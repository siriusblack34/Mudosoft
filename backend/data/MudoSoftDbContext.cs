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

        public DbSet<CommandResultRecord> CommandResultRecords { get; set; }
        public DbSet<ActionRecord> ActionRecords { get; set; }

        public DbSet<StoreDevice> StoreDevices { get; set; }
        public DbSet<Note> Notes { get; set; }
        public DbSet<ScheduledTask> ScheduledTasks { get; set; }
        public DbSet<StoreManager> StoreManagers { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //
            // DEVICE
            //
            modelBuilder.Entity<Device>()
                .Property(d => d.Id)
                .HasMaxLength(450);

            modelBuilder.Entity<DeviceMetric>()
                .HasOne(dm => dm.Device)
                .WithMany(d => d.Metrics)
                .HasForeignKey(dm => dm.DeviceId)
                .IsRequired();


            //
            // COMMAND RESULT
            //
            modelBuilder.Entity<CommandResultRecord>(e =>
            {
                e.HasKey(r => r.Id);

                e.Property(r => r.DeviceId)
                    .HasMaxLength(450);

                e.HasIndex(r => r.DeviceId);
                e.HasIndex(r => r.CommandId).IsUnique();
            });


            //
            // STORE DEVICE  (FINAL STABLE VERSION)
            //
            modelBuilder.Entity<StoreDevice>(e =>
            {
                // PK zaten [Key] annotation ile DeviceId
                e.Property(sd => sd.DeviceId)
                    .HasMaxLength(100);

                e.Property(sd => sd.StoreName)
                    .HasMaxLength(100);

                e.Property(sd => sd.DeviceType)
                    .HasMaxLength(10);

                e.Property(sd => sd.DeviceName)
                    .HasMaxLength(50);

                e.Property(sd => sd.CalculatedIpAddress)
                    .HasMaxLength(15);

                e.Property(sd => sd.DbConnectionString)
                    .HasMaxLength(256);

                // 🔥 EK PK / UNIQUE GEREKMİYOR
                // e.HasIndex(sd => new { sd.StoreCode, sd.DeviceType }).IsUnique();  <-- SİLİNDİ!
            });
        }
    }
}
