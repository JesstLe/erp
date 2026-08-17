using Erp.Domain.Authorization;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Organization;
using Erp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Store> Stores => Set<Store>();

    public DbSet<Erp.Domain.Organization.UserStore> UserStores => Set<Erp.Domain.Organization.UserStore>();

    public DbSet<RoleActionGrant> RoleActionGrants => Set<RoleActionGrant>();

    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();

    public DbSet<PriceBook> PriceBooks => Set<PriceBook>();

    public DbSet<PriceBookLine> PriceBookLines => Set<PriceBookLine>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Property(nameof(Entity.Version)).CurrentValue = entry.Entity.Version + 1;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentity(builder);
        ConfigureOrganization(builder);
        ConfigureAuthorization(builder);
        ConfigureCatalog(builder);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("identity_users");
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.UserName).HasColumnName("user_name").HasMaxLength(100);
            entity.Property(x => x.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(100);
            entity.Property(x => x.Email).HasColumnName("email").HasMaxLength(256);
            entity.Property(x => x.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
            entity.Property(x => x.EmailConfirmed).HasColumnName("email_confirmed");
            entity.Property(x => x.PasswordHash).HasColumnName("password_hash");
            entity.Property(x => x.SecurityStamp).HasColumnName("security_stamp");
            entity.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            entity.Property(x => x.PhoneNumber).HasColumnName("phone_number").HasMaxLength(32);
            entity.Property(x => x.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
            entity.Property(x => x.TwoFactorEnabled).HasColumnName("two_factor_enabled");
            entity.Property(x => x.LockoutEnd).HasColumnName("lockout_end");
            entity.Property(x => x.LockoutEnabled).HasColumnName("lockout_enabled");
            entity.Property(x => x.AccessFailedCount).HasColumnName("access_failed_count");
        });

        builder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("identity_roles");
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(64);
            entity.Property(x => x.NormalizedName).HasColumnName("normalized_name").HasMaxLength(64);
            entity.Property(x => x.ConcurrencyStamp).HasColumnName("concurrency_stamp");
            entity.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
            entity.HasIndex(x => x.NormalizedName).HasDatabaseName("ix_identity_roles_normalized_name").IsUnique(false);
        });

        builder.Entity<IdentityUserRole<Guid>>(entity =>
        {
            entity.ToTable("identity_user_roles");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
        });
        builder.Entity<IdentityUserClaim<Guid>>(entity =>
        {
            entity.ToTable("identity_user_claims");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.ClaimType).HasColumnName("claim_type");
            entity.Property(x => x.ClaimValue).HasColumnName("claim_value");
        });
        builder.Entity<IdentityUserLogin<Guid>>(entity =>
        {
            entity.ToTable("identity_user_logins");
            entity.Property(x => x.LoginProvider).HasColumnName("login_provider");
            entity.Property(x => x.ProviderKey).HasColumnName("provider_key");
            entity.Property(x => x.ProviderDisplayName).HasColumnName("provider_display_name");
            entity.Property(x => x.UserId).HasColumnName("user_id");
        });
        builder.Entity<IdentityUserToken<Guid>>(entity =>
        {
            entity.ToTable("identity_user_tokens");
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.LoginProvider).HasColumnName("login_provider");
            entity.Property(x => x.Name).HasColumnName("name");
            entity.Property(x => x.Value).HasColumnName("value");
        });
        builder.Entity<IdentityRoleClaim<Guid>>(entity =>
        {
            entity.ToTable("identity_role_claims");
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.RoleId).HasColumnName("role_id");
            entity.Property(x => x.ClaimType).HasColumnName("claim_type");
            entity.Property(x => x.ClaimValue).HasColumnName("claim_value");
        });
    }

    private static void ConfigureOrganization(ModelBuilder builder)
    {
        builder.Entity<Tenant>(entity =>
        {
            entity.ToTable("organization_tenants");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(32);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<Store>(entity =>
        {
            entity.ToTable("organization_stores");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(32);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(x => x.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        builder.Entity<Erp.Domain.Organization.UserStore>(entity =>
        {
            entity.ToTable("organization_user_stores");
            ConfigureBase(entity);
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.IsDefault).HasColumnName("is_default");
            entity.HasIndex(x => new { x.UserId, x.StoreId }).IsUnique();
        });
    }

    private static void ConfigureAuthorization(ModelBuilder builder)
    {
        builder.Entity<RoleActionGrant>(entity =>
        {
            entity.ToTable("authorization_role_permissions");
            ConfigureBase(entity);
            entity.Property(x => x.RoleId).HasColumnName("role_id");
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(128);
            entity.HasIndex(x => new { x.RoleId, x.Action }).IsUnique();
        });
    }

    private static void ConfigureCatalog(ModelBuilder builder)
    {
        builder.Entity<ServiceItem>(entity =>
        {
            entity.ToTable("catalog_service_items");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(x => x.StandardDurationMinutes).HasColumnName("standard_duration_minutes");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        builder.Entity<PriceBook>(entity =>
        {
            entity.ToTable("catalog_price_books");
            ConfigureBase(entity);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(x => x.Revision).HasColumnName("revision");
            entity.Property(x => x.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.PublishedAtUtc).HasColumnName("published_at_utc");
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.PriceBookId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<PriceBookLine>(entity =>
        {
            entity.ToTable("catalog_price_book_lines");
            ConfigureBase(entity);
            entity.Property(x => x.PriceBookId).HasColumnName("price_book_id");
            entity.Property(x => x.ServiceItemId).HasColumnName("service_item_id");
            entity.Property(x => x.UnitPriceMinor).HasColumnName("unit_price_minor");
            entity.HasIndex(x => new { x.PriceBookId, x.ServiceItemId }).IsUnique();
        });
    }

    private static void ConfigureBase<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : Entity
    {
        entity.HasKey(x => x.Id);
        entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        entity.Property(x => x.TenantId).HasColumnName("tenant_id");
        entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        entity.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc");
        entity.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        entity.HasIndex(x => x.TenantId);
    }
}
