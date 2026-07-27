using Microsoft.EntityFrameworkCore;
using Afrobotics.Bit.Api.Models;

namespace Afrobotics.Bit.Api.Data
{
    public class PostgresDbContext : DbContext
    {
        public PostgresDbContext(DbContextOptions<PostgresDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<ContentItem> ContentItems { get; set; } = null!;
        public DbSet<SceneItem> SceneItems { get; set; } = null!;
        public DbSet<SurfaceItem> SurfaceItems { get; set; } = null!;
        public DbSet<CampaignItem> Campaigns { get; set; } = null!;
        public DbSet<CreativeAsset> CreativeAssets { get; set; } = null!;
        public DbSet<AdSlotItem> AdSlots { get; set; } = null!;
        public DbSet<ApprovalItem> Approvals { get; set; } = null!;
        public DbSet<RenderItem> Renders { get; set; } = null!;
        public DbSet<EventLog> EventLogs { get; set; } = null!;
        public DbSet<AlarmItem> Alarms { get; set; } = null!;
        public DbSet<UsageRecord> UsageRecords { get; set; } = null!;
        public DbSet<NotificationItem> Notifications { get; set; } = null!;
        public DbSet<PlatformSetting> PlatformSettings { get; set; } = null!;
        public DbSet<RoleRequest> RoleRequests { get; set; } = null!;
        public DbSet<BrandSafetyRule> BrandSafetyRules { get; set; } = null!;
        public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Set up indices and constraints for optimal PostgreSQL query performance (MReq 25)
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // ── ContentItem indexes ──
            modelBuilder.Entity<ContentItem>()
                .HasIndex(c => c.StorageKey);
            modelBuilder.Entity<ContentItem>()
                .HasIndex(c => c.CampaignId);
            modelBuilder.Entity<ContentItem>()
                .HasIndex(c => c.IngestionStatus);
            modelBuilder.Entity<ContentItem>()
                .HasIndex(c => c.DetectionJobId);

            // ── CampaignItem indexes ──
            modelBuilder.Entity<CampaignItem>()
                .HasIndex(c => c.NamingStructureCode);
            modelBuilder.Entity<CampaignItem>()
                .HasIndex(c => c.Status);

            // ── SceneItem indexes ──
            modelBuilder.Entity<SceneItem>()
                .HasIndex(s => s.ContentId);

            // ── SurfaceItem indexes ──
            modelBuilder.Entity<SurfaceItem>()
                .HasIndex(sf => sf.SceneId);
            modelBuilder.Entity<SurfaceItem>()
                .HasIndex(sf => sf.Status);

            // ── AdSlotItem indexes ──
            modelBuilder.Entity<AdSlotItem>()
                .HasIndex(a => a.SurfaceId);

            // ── ApprovalItem indexes ──
            modelBuilder.Entity<ApprovalItem>()
                .HasIndex(a => a.AdSlotId);

            // ── RenderItem indexes ──
            modelBuilder.Entity<RenderItem>()
                .HasIndex(r => r.CampaignId);
            modelBuilder.Entity<RenderItem>()
                .HasIndex(r => r.ContentId);
            modelBuilder.Entity<RenderItem>()
                .HasIndex(r => r.SurfaceId);

            // ── CreativeAsset indexes ──
            modelBuilder.Entity<CreativeAsset>()
                .HasIndex(a => a.CampaignId);

            // ── EventLog / Usage / Notification indexes ──
            modelBuilder.Entity<EventLog>()
                .HasIndex(l => l.EventCode);
            modelBuilder.Entity<UsageRecord>()
                .HasIndex(r => r.Timestamp);
            modelBuilder.Entity<UsageRecord>()
                .HasIndex(r => r.UserId);
            modelBuilder.Entity<NotificationItem>()
                .HasIndex(n => n.Timestamp);
            modelBuilder.Entity<NotificationItem>()
                .HasIndex(n => n.RecipientEmail);

            // Configure decimal precision
            modelBuilder.Entity<CampaignItem>()
                .Property(c => c.TotalBudget)
                .HasPrecision(18, 2);

            modelBuilder.Entity<AdSlotItem>()
                .Property(s => s.PricingValue)
                .HasPrecision(18, 2);

            // ── Cascade delete relationships ──
            // ContentItem → SceneItem
            modelBuilder.Entity<SceneItem>()
                .HasOne<ContentItem>()
                .WithMany()
                .HasForeignKey(s => s.ContentId)
                .OnDelete(DeleteBehavior.Cascade);

            // SceneItem → SurfaceItem
            modelBuilder.Entity<SurfaceItem>()
                .HasOne<SceneItem>()
                .WithMany()
                .HasForeignKey(sf => sf.SceneId)
                .OnDelete(DeleteBehavior.Cascade);

            // SurfaceItem → AdSlotItem
            modelBuilder.Entity<AdSlotItem>()
                .HasOne<SurfaceItem>()
                .WithMany()
                .HasForeignKey(a => a.SurfaceId)
                .OnDelete(DeleteBehavior.Cascade);

            // AdSlotItem → ApprovalItem
            modelBuilder.Entity<ApprovalItem>()
                .HasOne<AdSlotItem>()
                .WithMany()
                .HasForeignKey(a => a.AdSlotId)
                .OnDelete(DeleteBehavior.Cascade);

            // ContentItem → RenderItem
            modelBuilder.Entity<RenderItem>()
                .HasOne<ContentItem>()
                .WithMany()
                .HasForeignKey(r => r.ContentId)
                .OnDelete(DeleteBehavior.Cascade);

            // SurfaceItem → RenderItem (additional FK — set to null on surface delete)
            modelBuilder.Entity<RenderItem>()
                .HasOne<SurfaceItem>()
                .WithMany()
                .HasForeignKey(r => r.SurfaceId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
