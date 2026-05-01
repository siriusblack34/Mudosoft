using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Models;

namespace Orchestra.Backend.Data
{
    public class OrchestraDbContext : DbContext
    {
        public OrchestraDbContext(DbContextOptions<OrchestraDbContext> options)
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
        public DbSet<VncSessionLog> VncSessionLogs { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<DeviceStatusChange> DeviceStatusChanges { get; set; }
        public DbSet<AgendaItem> AgendaItems { get; set; }
        public DbSet<RouterLatencySample> RouterLatencySamples { get; set; }
        public DbSet<StoreNetworkInfo> StoreNetworkInfos { get; set; }


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

            modelBuilder.Entity<Note>(e =>
            {
                e.Property(n => n.OwnerUsername)
                    .HasMaxLength(50);

                e.Property(n => n.Title)
                    .HasMaxLength(200);

                e.HasIndex(n => n.OwnerUsername);
                e.HasIndex(n => n.IsShared);
            });

            modelBuilder.Entity<AgendaItem>(e =>
            {
                e.Property(a => a.Title)
                    .HasMaxLength(200);

                e.Property(a => a.Status)
                    .HasMaxLength(20);

                e.Property(a => a.Priority)
                    .HasMaxLength(20);

                e.Property(a => a.Category)
                    .HasMaxLength(40);

                e.Property(a => a.CreatedBy)
                    .HasMaxLength(100);

                e.HasIndex(a => a.Status);
                e.HasIndex(a => a.Priority);
                e.HasIndex(a => a.DueDate);
                e.HasIndex(a => a.UpdatedAt);
            });

            //
            // DEVICE STATUS CHANGE (ag teshis icin durum gecis logu)
            //
            modelBuilder.Entity<DeviceStatusChange>(e =>
            {
                e.HasIndex(c => c.StoreCode);
                e.HasIndex(c => c.DeviceId);
                e.HasIndex(c => c.ChangedAt);
                e.HasIndex(c => new { c.StoreCode, c.ChangedAt });
                e.HasIndex(c => new { c.DeviceId, c.ChangedAt });
            });

            modelBuilder.Entity<VncSessionLog>(e =>
            {
                e.HasKey(l => l.Id);
                e.Property(l => l.DeviceId).HasMaxLength(450);
                e.Property(l => l.Username).HasMaxLength(256);
                e.Property(l => l.SessionId).HasMaxLength(64);
                e.HasIndex(l => l.DeviceId);
                e.HasIndex(l => l.StartedAtUtc);
                e.HasIndex(l => l.SessionId);
            });

            //
            // ROUTER LATENCY SAMPLE (karasal / 4.5G tespiti icin)
            //
            modelBuilder.Entity<RouterLatencySample>(e =>
            {
                e.HasKey(s => s.Id);
                e.HasIndex(s => s.StoreCode);
                e.HasIndex(s => s.SampledAt);
                e.HasIndex(s => new { s.StoreCode, s.SampledAt });
                e.HasIndex(s => new { s.DeviceId, s.SampledAt });
            });

            //
            // STORE NETWORK INFO (taahhut edilen karasal hat hizi)
            //
            modelBuilder.Entity<StoreNetworkInfo>(e =>
            {
                e.HasKey(s => s.StoreCode);
            });
        }
    }
}
