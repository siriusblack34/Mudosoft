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
        public DbSet<StoreServiceIncident> StoreServiceIncidents => Set<StoreServiceIncident>();

        public DbSet<InventoryAsset> InventoryAssets => Set<InventoryAsset>();
        public DbSet<InventoryImportBatch> InventoryImportBatches => Set<InventoryImportBatch>();
        public DbSet<StoreNameMapping> StoreNameMappings => Set<StoreNameMapping>();
        public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
        public DbSet<PendingUserInstall> PendingUserInstalls => Set<PendingUserInstall>();
        public DbSet<DcLogCursor> DcLogCursors => Set<DcLogCursor>();

        public DbSet<StoreOpening> StoreOpenings => Set<StoreOpening>();
        public DbSet<StoreOpeningItem> StoreOpeningItems => Set<StoreOpeningItem>();
        public DbSet<StoreOpeningTemplate> StoreOpeningTemplates => Set<StoreOpeningTemplate>();
        public DbSet<StoreOpeningTemplateItem> StoreOpeningTemplateItems => Set<StoreOpeningTemplateItem>();
        public DbSet<StoreOpeningActivity> StoreOpeningActivities => Set<StoreOpeningActivity>();


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

            //
            // STORE SERVICE INCIDENTS (agentless WMI/CIM service monitoring)
            //
            modelBuilder.Entity<StoreServiceIncident>(e =>
            {
                e.HasKey(i => i.Id);
                e.HasIndex(i => i.StoreCode);
                e.HasIndex(i => i.DeviceId);
                e.HasIndex(i => i.ServiceName);
                e.HasIndex(i => i.ResolvedAt);
                e.HasIndex(i => i.LastDetectedAt);
                e.HasIndex(i => new { i.StoreCode, i.ResolvedAt });
                e.HasIndex(i => new { i.DeviceId, i.ServiceName, i.ResolvedAt });
                e.HasIndex(i => new { i.DeviceId, i.ServiceName })
                    .IsUnique()
                    .HasFilter("\"ResolvedAt\" IS NULL");
            });

            //
            // INVENTORY (SDP envanter modulu)
            //
            modelBuilder.Entity<InventoryAsset>(e =>
            {
                e.HasIndex(a => a.AssetName).IsUnique();
                e.HasIndex(a => a.StoreCode);
                e.HasIndex(a => a.ProductType);
                e.HasIndex(a => a.AssetState);
                e.HasIndex(a => a.ImportBatchId);
                e.Property(a => a.PurchaseCost).HasPrecision(18, 2);

                e.HasOne(a => a.ImportBatch)
                    .WithMany()
                    .HasForeignKey(a => a.ImportBatchId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<InventoryImportBatch>(e =>
            {
                e.HasIndex(b => b.ImportedAt);
            });

            modelBuilder.Entity<StoreNameMapping>(e =>
            {
                e.HasIndex(m => m.RawName).IsUnique();
                e.HasIndex(m => m.StoreCode);
            });

            //
            // PENDING USER INSTALL (AD user-based deferred install)
            //
            modelBuilder.Entity<PendingUserInstall>(e =>
            {
                e.Property(p => p.SamAccountName).IsRequired();
                e.HasIndex(p => p.SamAccountName);
                e.HasIndex(p => p.Status);
                e.HasIndex(p => p.ExpiresAt);
                e.HasIndex(p => new { p.Status, p.SamAccountName });
            });

            modelBuilder.Entity<DcLogCursor>(e =>
            {
                e.Property(c => c.DcName).IsRequired();
            });

            //
            // STORE OPENING CHECKLIST
            //
            modelBuilder.Entity<StoreOpening>(e =>
            {
                e.HasIndex(o => o.StoreCode);
                e.HasIndex(o => o.Status);
                e.HasIndex(o => o.TargetOpeningDate);
                e.HasMany(o => o.Items)
                    .WithOne(i => i.StoreOpening)
                    .HasForeignKey(i => i.StoreOpeningId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StoreOpeningItem>(e =>
            {
                e.HasIndex(i => i.StoreOpeningId);
                e.HasIndex(i => new { i.StoreOpeningId, i.CategoryName });
                e.HasIndex(i => i.AssignedRole);
                e.HasIndex(i => i.Status);
            });

            modelBuilder.Entity<StoreOpeningTemplate>(e =>
            {
                e.HasIndex(t => t.IsDefault);
                e.HasMany(t => t.Items)
                    .WithOne(i => i.Template)
                    .HasForeignKey(i => i.TemplateId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<StoreOpeningTemplateItem>(e =>
            {
                e.HasIndex(i => i.TemplateId);
            });

            modelBuilder.Entity<StoreOpeningActivity>(e =>
            {
                e.HasIndex(a => a.StoreOpeningId);
                e.HasIndex(a => a.CreatedAt);
            });

            //
            // ACTIVITY LOG (audit)
            //
            modelBuilder.Entity<ActivityLog>(e =>
            {
                e.HasIndex(a => a.CreatedAt);
                e.HasIndex(a => a.Username);
                e.HasIndex(a => a.Category);
                e.HasIndex(a => new { a.Category, a.CreatedAt });
            });
        }
    }
}
