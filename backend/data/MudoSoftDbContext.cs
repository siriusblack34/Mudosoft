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
        public DbSet<StoreOfflineLog> StoreOfflineLogs { get; set; }
        public DbSet<CollectorReport> CollectorReports { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<LoginHistory> LoginHistories { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //
            // DEVICE
            //
            modelBuilder.Entity<Device>(e =>
            {
                e.Property(d => d.Id).HasMaxLength(450);
                e.HasIndex(d => d.Online);
                e.HasIndex(d => d.LastSeen);
                e.HasIndex(d => new { d.Online, d.LastSeen });
            });

            modelBuilder.Entity<DeviceMetric>(e =>
            {
                e.HasOne(dm => dm.Device)
                    .WithMany(d => d.Metrics)
                    .HasForeignKey(dm => dm.DeviceId)
                    .IsRequired();
                e.HasIndex(dm => dm.DeviceId);
                e.HasIndex(dm => new { dm.DeviceId, dm.TimestampUtc });
            });


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

                e.HasIndex(sd => sd.StoreCode);
            });

            //
            // STORE OFFLINE LOG
            //
            modelBuilder.Entity<StoreOfflineLog>(e =>
            {
                e.HasIndex(l => l.StoreCode);
                e.HasIndex(l => l.OfflineAt);
                e.HasIndex(l => l.OnlineAt);
            });

            //
            // COLLECTOR REPORT
            //
            modelBuilder.Entity<CollectorReport>(e =>
            {
                e.HasIndex(r => r.DeviceId);
                e.HasIndex(r => r.CollectorName);
                e.HasIndex(r => new { r.DeviceId, r.TimestampUtc });
                e.HasIndex(r => new { r.DeviceId, r.CollectorName, r.TimestampUtc });
            });

            //
            // USER
            //
            modelBuilder.Entity<User>(e =>
            {
                e.HasIndex(u => u.Username).IsUnique();
            });

            modelBuilder.Entity<LoginHistory>(e =>
            {
                e.HasIndex(l => l.UserId);
                e.HasIndex(l => l.LoginAt);
            });
        }
    }
}
