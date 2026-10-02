using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Venue> Venues { get; set; } = null!;
    public DbSet<Section> Sections { get; set; } = null!;
    public DbSet<Resource> Resources { get; set; } = null!;
    public DbSet<ResourceGroup> ResourceGroups { get; set; } = null!;
    public DbSet<ResourceGroupMembership> ResourceGroupMemberships { get; set; } = null!;
    public DbSet<Booking> Bookings { get; set; } = null!;
    public DbSet<AdminCredential> AdminCredentials { get; set; } = null!;
    public DbSet<EmailSettings> EmailSettings { get; set; } = null!;
    public DbSet<BrandSettings> BrandSettings { get; set; } = null!;
    public DbSet<VenueHighlight> Highlights { get; set; } = null!;
    public DbSet<SocialLink> SocialLinks { get; set; } = null!;
    public DbSet<EmailFailure> EmailFailures { get; set; } = null!;
    public DbSet<AdminNotification> AdminNotifications { get; set; } = null!;
    public DbSet<AdminPushSubscription> AdminPushSubscriptions { get; set; } = null!;
    public DbSet<GuestPushSubscription> GuestPushSubscriptions { get; set; } = null!;
    public DbSet<AdminAuditEntry> AdminAuditEntries { get; set; } = null!;
    public DbSet<AdminApiKey> AdminApiKeys { get; set; } = null!;
    public DbSet<NativeClientStat> NativeClientStats { get; set; } = null!;
    public DbSet<WaitlistEntry> WaitlistEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Force UTC for all DateTime properties
        var dateTimeConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        var nullableDateTimeConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
            v => !v.HasValue ? v : (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()),
            v => !v.HasValue ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                {
                    property.SetValueConverter(dateTimeConverter);
                }
                else if (property.ClrType == typeof(DateTime?))
                {
                    property.SetValueConverter(nullableDateTimeConverter);
                }
            }
        }

        modelBuilder.Entity<Venue>(rb =>
        {
            rb.HasKey(r => r.Id);
            rb.Property(r => r.Name).IsRequired();
            rb.HasMany(r => r.Sections)
              .WithOne(s => s.Venue)
              .HasForeignKey(s => s.VenueId)
              .OnDelete(DeleteBehavior.Cascade);
            rb.HasMany(r => r.Groups)
              .WithOne(g => g.Venue!)
              .HasForeignKey(g => g.VenueId)
              .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Section>(sb =>
        {
            sb.HasKey(s => s.Id);
            sb.Property(s => s.Name).IsRequired();
            sb.HasMany(s => s.Resources)
              .WithOne(t => t.Section)
              .HasForeignKey(t => t.SectionId)
              .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Resource>(tb =>
        {
            tb.HasKey(t => t.Id);
            tb.Property(t => t.Capacity).IsRequired();
            tb.Property(t => t.Name);
        });

        modelBuilder.Entity<Booking>(bb =>
        {
            bb.HasKey(b => b.Id);
            bb.HasOne(b => b.Resource).WithMany().HasForeignKey(b => b.ResourceId).OnDelete(DeleteBehavior.SetNull);
            bb.HasOne(b => b.Section).WithMany().HasForeignKey(b => b.SectionId).OnDelete(DeleteBehavior.SetNull);
            bb.HasOne(b => b.Venue).WithMany().HasForeignKey(b => b.VenueId);
            bb.HasOne(b => b.ResourceGroup).WithMany().HasForeignKey(b => b.ResourceGroupId).OnDelete(DeleteBehavior.SetNull);
            bb.Property(b => b.Status).HasConversion<string>().HasMaxLength(BookingStatusExtensions.MaxLength);
            bb.Property(b => b.PreviousStatus).HasConversion<string>().HasMaxLength(BookingStatusExtensions.MaxLength);
        });

        modelBuilder.Entity<ResourceGroup>(gb =>
        {
            gb.HasKey(g => g.Id);
            gb.Property(g => g.CombinedCapacity).IsRequired();
            gb.HasMany(g => g.Members)
              .WithOne(m => m.Group!)
              .HasForeignKey(m => m.ResourceGroupId)
              .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResourceGroupMembership>(mb =>
        {
            // Composite PK — a (group, resource) pair is unique by definition; the unique index on
            // ResourceId below enforces "a resource belongs to at most one group" at the DB level (also
            // enforced in service code, since the in-memory provider used by tests ignores constraints).
            mb.HasKey(m => new { m.ResourceGroupId, m.ResourceId });
            mb.HasOne(m => m.Resource).WithMany().HasForeignKey(m => m.ResourceId).OnDelete(DeleteBehavior.Cascade);
            mb.HasIndex(m => m.ResourceId).IsUnique();
        });

        modelBuilder.Entity<AdminCredential>(a =>
        {
            a.HasKey(x => x.Id);
            // Emails are lower-cased on every write path (bootstrap, create-user, change-email),
            // so an ordinary unique index is enough to make "one account per address"
            // case-insensitive — no NOCASE collation change, which SQLite can only apply by
            // rebuilding the resource.
            a.HasIndex(x => x.Email).IsUnique();
            a.Property(x => x.DisplayName).HasMaxLength(UserFields.MaxDisplayNameLength);
            a.Property(x => x.Role).HasMaxLength(UserFields.MaxRoleLength).HasDefaultValue(UserRoles.Owner);
            a.Property(x => x.IsActive).HasDefaultValue(true);
            a.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<AdminNotification>(n =>
        {
            n.HasKey(x => x.Id);
            n.HasOne(x => x.Venue).WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            n.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            n.HasIndex(x => new { x.VenueId, x.CreatedAt });
            n.HasIndex(x => new { x.VenueId, x.IsRead });
        });

        modelBuilder.Entity<AdminAuditEntry>(e =>
        {
            e.HasKey(x => x.Id);
            // SetNull, not Cascade: the denormalized actor email and role are what keep an entry
            // readable, and deleting the account must not delete the record of what it did.
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.ActorEmail).IsRequired().HasMaxLength(AuditFields.MaxActorEmailLength);
            e.Property(x => x.ActorDisplayName).HasMaxLength(AuditFields.MaxActorDisplayNameLength);
            e.Property(x => x.ActorRole).IsRequired().HasMaxLength(AuditFields.MaxRoleLength);
            e.Property(x => x.Action).IsRequired().HasMaxLength(AuditFields.MaxActionLength);
            e.Property(x => x.TargetType).HasMaxLength(AuditFields.MaxTargetTypeLength);
            e.Property(x => x.TargetId).HasMaxLength(AuditFields.MaxTargetIdLength);
            e.Property(x => x.TargetLabel).HasMaxLength(AuditFields.MaxTargetLabelLength);
            e.Property(x => x.Summary).HasMaxLength(AuditFields.MaxSummaryLength);
            e.Property(x => x.ChangesJson).HasMaxLength(AuditFields.MaxChangesJsonLength);
            e.Property(x => x.HttpMethod).IsRequired().HasMaxLength(AuditFields.MaxHttpMethodLength);
            e.Property(x => x.Path).IsRequired().HasMaxLength(AuditFields.MaxPathLength);
            e.Property(x => x.IpAddress).HasMaxLength(AuditFields.MaxIpAddressLength);
            e.Property(x => x.UserAgent).HasMaxLength(AuditFields.MaxUserAgentLength);
            // The list is always newest-first; the filters are the other three axes.
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.ActorUserId, x.OccurredAt });
            e.HasIndex(x => new { x.VenueId, x.OccurredAt });
            e.HasIndex(x => x.Action);
        });

        modelBuilder.Entity<AdminApiKey>(k =>
        {
            k.HasKey(x => x.Id);
            // Cascade so deleting a user through EF takes their keys with it. It does not
            // cover the demo reset: that applies raw SQL under PRAGMA foreign_keys=OFF, where
            // no cascade fires, so scripts/demo_data.py deletes AdminApiKeys explicitly.
            k.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            k.Property(x => x.Name).IsRequired().HasMaxLength(ApiKeyFields.MaxNameLength);
            k.Property(x => x.KeyHash).IsRequired().HasMaxLength(ApiKeyFields.KeyHashLength);
            k.Property(x => x.Prefix).IsRequired().HasMaxLength(ApiKeyFields.MaxPrefixLength);
            k.Property(x => x.ScopesJson).IsRequired().HasMaxLength(ApiKeyFields.MaxScopesJsonLength);
            k.HasIndex(x => x.KeyHash).IsUnique();
            k.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<NativeClientStat>(n =>
        {
            n.HasKey(x => x.Id);
            n.Property(x => x.Platform).IsRequired().HasMaxLength(NativeClientIdentity.MaxPlatformLength);
            n.Property(x => x.AppVersion).IsRequired().HasMaxLength(NativeAppVersion.MaxLength);
            // One row per bucket: the flush pass upserts onto it rather than appending, so the
            // resource stays proportional to (platforms x live versions x retained days).
            n.HasIndex(x => new { x.Platform, x.AppVersion, x.Day }).IsUnique();
        });

        modelBuilder.Entity<AdminPushSubscription>(s =>
        {
            s.HasKey(x => x.Id);
            // One row per browser, not per (browser, venue): the fan-out sends every
            // location's notifications to every subscription, so a second row per location
            // would only push the same payload to the same browser twice.
            s.HasIndex(x => x.Endpoint).IsUnique();
        });

        modelBuilder.Entity<GuestPushSubscription>(g =>
        {
            g.HasKey(x => x.Id);
            g.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            g.Property(x => x.Channel).IsRequired().HasMaxLength(GuestPushFields.MaxChannelLength);
            g.Property(x => x.Endpoint).IsRequired().HasMaxLength(GuestPushFields.MaxEndpointLength);
            g.Property(x => x.P256dh).HasMaxLength(GuestPushFields.MaxKeyLength);
            g.Property(x => x.Auth).HasMaxLength(GuestPushFields.MaxKeyLength);
            g.Property(x => x.Locale).IsRequired().HasMaxLength(GuestPushFields.MaxLocaleLength);
            g.HasIndex(x => new { x.BookingId, x.Endpoint }).IsUnique();
        });

        modelBuilder.Entity<WaitlistEntry>(w =>
        {
            w.HasKey(x => x.Id);
            w.HasOne(x => x.Venue).WithMany().HasForeignKey(x => x.VenueId).OnDelete(DeleteBehavior.Cascade);
            w.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.SetNull);
            w.Property(x => x.Ref).IsRequired().HasMaxLength(WaitlistFields.RefLength);
            w.Property(x => x.Name).IsRequired().HasMaxLength(WaitlistFields.MaxNameLength);
            w.Property(x => x.Email).HasMaxLength(ContactLimits.MaxEmailLength);
            w.Property(x => x.Locale).IsRequired().HasMaxLength(GuestPushFields.MaxLocaleLength);
            w.Property(x => x.PushChannel).HasMaxLength(GuestPushFields.MaxChannelLength);
            w.Property(x => x.PushEndpoint).HasMaxLength(GuestPushFields.MaxEndpointLength);
            w.Property(x => x.PushP256dh).HasMaxLength(GuestPushFields.MaxKeyLength);
            w.Property(x => x.PushAuth).HasMaxLength(GuestPushFields.MaxKeyLength);
            w.Property(x => x.Status).HasConversion<string>().HasMaxLength(WaitlistFields.MaxStatusLength);
            w.HasIndex(x => x.Ref).IsUnique();
            w.HasIndex(x => new { x.VenueId, x.Status });
        });
    }
}
