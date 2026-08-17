using Erp.Domain.Authorization;
using Erp.Domain.Catalog;
using Erp.Domain.Common;
using Erp.Domain.Customers;
using Erp.Domain.Facilities;
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

    public DbSet<FacilityGroup> FacilityGroups => Set<FacilityGroup>();
    public DbSet<FacilityType> FacilityTypes => Set<FacilityType>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<FacilitySession> FacilitySessions => Set<FacilitySession>();
    public DbSet<FacilitySessionPause> FacilitySessionPauses => Set<FacilitySessionPause>();
    public DbSet<FacilityCleaningTask> FacilityCleaningTasks => Set<FacilityCleaningTask>();
    public DbSet<AuditEventRecord> AuditEvents => Set<AuditEventRecord>();
    public DbSet<IdempotencyCommandRecord> IdempotencyCommands => Set<IdempotencyCommandRecord>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MemberCardType> MemberCardTypes => Set<MemberCardType>();
    public DbSet<MemberCard> MemberCards => Set<MemberCard>();
    public DbSet<MemberAccount> MemberAccounts => Set<MemberAccount>();
    public DbSet<MemberAccountLedger> MemberAccountLedgers => Set<MemberAccountLedger>();

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
        ConfigureFacilities(builder);
        ConfigureCustomers(builder);
        ConfigureSystemRecords(builder);
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

    private static void ConfigureFacilities(ModelBuilder builder)
    {
        builder.Entity<FacilityGroup>(entity =>
        {
            entity.ToTable("facility_groups");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50);
            entity.Property(x => x.SortOrder).HasColumnName("sort_order");
        });
        builder.Entity<FacilityType>(entity =>
        {
            entity.ToTable("facility_types");
            ConfigureBase(entity);
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50);
        });
        builder.Entity<Facility>(entity =>
        {
            entity.ToTable("facilities");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.GroupId).HasColumnName("group_id");
            entity.Property(x => x.FacilityTypeId).HasColumnName("facility_type_id");
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50);
            entity.Property(x => x.SortOrder).HasColumnName("sort_order");
            entity.Property(x => x.DefaultCleaningMinutes).HasColumnName("default_cleaning_minutes");
            entity.Property(x => x.AllowReservation).HasColumnName("allow_reservation");
            entity.Property(x => x.LifecycleStatus).HasColumnName("lifecycle_status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.StoreId, x.Code }).IsUnique();
        });
        builder.Entity<Visit>(entity =>
        {
            entity.ToTable("visits");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.VisitNo).HasColumnName("visit_no").HasMaxLength(40);
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.ExpectedDurationMinutes).HasColumnName("expected_duration_minutes");
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(x => x.ArrivedAtUtc).HasColumnName("arrived_at_utc");
            entity.Property(x => x.ServiceEndedAtUtc).HasColumnName("service_ended_at_utc");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.TenantId, x.VisitNo }).IsUnique();
        });
        builder.Entity<FacilitySession>(entity =>
        {
            entity.ToTable("facility_sessions");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.FacilityId).HasColumnName("facility_id");
            entity.Property(x => x.VisitId).HasColumnName("visit_id");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(x => x.EndedAtUtc).HasColumnName("ended_at_utc");
            entity.Property(x => x.StartedByUserId).HasColumnName("started_by_user_id");
            entity.Property(x => x.StartCommandId).HasColumnName("start_command_id");
            entity.Property(x => x.EndReason).HasColumnName("end_reason").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.SwitchGroupId).HasColumnName("switch_group_id");
            entity.HasMany(x => x.Pauses).WithOne().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Pauses).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Entity<FacilitySessionPause>(entity =>
        {
            entity.ToTable("facility_session_pauses");
            ConfigureBase(entity);
            entity.Property(x => x.SessionId).HasColumnName("session_id");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(x => x.EndedAtUtc).HasColumnName("ended_at_utc");
            entity.Property(x => x.StartedByUserId).HasColumnName("started_by_user_id");
            entity.Property(x => x.CommandId).HasColumnName("command_id");
        });
        builder.Entity<FacilityCleaningTask>(entity =>
        {
            entity.ToTable("facility_cleaning_tasks");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.FacilityId).HasColumnName("facility_id");
            entity.Property(x => x.SessionId).HasColumnName("session_id");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.DueAtUtc).HasColumnName("due_at_utc");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(x => x.CompletedByUserId).HasColumnName("completed_by_user_id");
        });
    }

    private static void ConfigureCustomers(ModelBuilder builder)
    {
        builder.Entity<Customer>(entity =>
        {
            entity.ToTable("customers");
            ConfigureBase(entity);
            entity.Property(x => x.HomeStoreId).HasColumnName("home_store_id");
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(x => x.MobileCiphertext).HasColumnName("mobile_ciphertext").HasMaxLength(2048);
            entity.Property(x => x.MobileLookupHash).HasColumnName("mobile_lookup_hash");
            entity.Property(x => x.MobileLastFour).HasColumnName("mobile_last_four").HasMaxLength(4);
            entity.Property(x => x.Gender).HasColumnName("gender").HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.BirthDate).HasColumnName("birth_date");
            entity.Property(x => x.SourceCode).HasColumnName("source_code").HasMaxLength(40);
            entity.Property(x => x.ServiceNotificationConsent).HasColumnName("service_notification_consent");
            entity.Property(x => x.MarketingConsent).HasColumnName("marketing_consent");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.MobileLookupHash });
            entity.HasIndex(x => new { x.HomeStoreId, x.CreatedAtUtc });
        });
        builder.Entity<MemberCardType>(entity =>
        {
            entity.ToTable("membership_card_types");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(80);
            entity.Property(x => x.ValidityDays).HasColumnName("validity_days");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        builder.Entity<MemberCard>(entity =>
        {
            entity.ToTable("membership_cards");
            ConfigureBase(entity);
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.CardTypeId).HasColumnName("card_type_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.CardNo).HasColumnName("card_no").HasMaxLength(40);
            entity.Property(x => x.ValidFrom).HasColumnName("valid_from");
            entity.Property(x => x.ValidTo).HasColumnName("valid_to");
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.CardNo }).IsUnique();
            entity.HasIndex(x => x.CustomerId);
        });
        builder.Entity<MemberAccount>(entity =>
        {
            entity.ToTable("member_accounts");
            ConfigureBase(entity);
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.CardId).HasColumnName("card_id");
            entity.Property(x => x.AccountType).HasColumnName("account_type").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.BalanceUnits).HasColumnName("balance_units");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.CardId, x.AccountType }).IsUnique();
            entity.HasIndex(x => x.CustomerId);
        });
        builder.Entity<MemberAccountLedger>(entity =>
        {
            entity.ToTable("member_account_ledgers");
            ConfigureBase(entity);
            entity.Property(x => x.AccountId).HasColumnName("account_id");
            entity.Property(x => x.BusinessType).HasColumnName("business_type").HasMaxLength(40);
            entity.Property(x => x.BusinessId).HasColumnName("business_id");
            entity.Property(x => x.Direction).HasColumnName("direction").HasConversion<string>().HasMaxLength(12);
            entity.Property(x => x.Units).HasColumnName("units");
            entity.Property(x => x.BalanceBefore).HasColumnName("balance_before");
            entity.Property(x => x.BalanceAfter).HasColumnName("balance_after");
            entity.Property(x => x.CommandId).HasColumnName("command_id");
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
            entity.HasIndex(x => new { x.AccountId, x.OccurredAtUtc });
        });
    }

    private static void ConfigureSystemRecords(ModelBuilder builder)
    {
        builder.Entity<AuditEventRecord>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.OperatorId).HasColumnName("operator_id");
            entity.Property(x => x.Action).HasColumnName("action").HasMaxLength(128);
            entity.Property(x => x.EntityType).HasColumnName("entity_type").HasMaxLength(80);
            entity.Property(x => x.EntityId).HasColumnName("entity_id");
            entity.Property(x => x.PreviousState).HasColumnName("previous_state").HasMaxLength(40);
            entity.Property(x => x.CurrentState).HasColumnName("current_state").HasMaxLength(40);
            entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(x => x.TraceId).HasColumnName("trace_id").HasMaxLength(64);
            entity.Property(x => x.RequestId).HasColumnName("request_id");
            entity.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
            entity.Property(x => x.OccurredAtUtc).HasColumnName("occurred_at_utc");
        });
        builder.Entity<IdempotencyCommandRecord>(entity =>
        {
            entity.ToTable("idempotency_commands");
            entity.HasKey(x => x.CommandId);
            entity.Property(x => x.CommandId).HasColumnName("command_id").ValueGeneratedNever();
            entity.Property(x => x.TenantId).HasColumnName("tenant_id");
            entity.Property(x => x.OperatorId).HasColumnName("operator_id");
            entity.Property(x => x.RequestHash).HasColumnName("request_hash");
            entity.Property(x => x.ResponseStatus).HasColumnName("response_status");
            entity.Property(x => x.ResponseBody).HasColumnName("response_body").HasColumnType("jsonb");
            entity.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
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
