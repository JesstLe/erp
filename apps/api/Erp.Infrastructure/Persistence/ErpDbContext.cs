using Erp.Domain.Authorization;
using Erp.Domain.Catalog;
using Erp.Domain.Cashier;
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

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<EmployeeStore> EmployeeStores => Set<EmployeeStore>();

    public DbSet<RoleActionGrant> RoleActionGrants => Set<RoleActionGrant>();

    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();

    public DbSet<ProductItem> ProductItems => Set<ProductItem>();

    public DbSet<PriceBook> PriceBooks => Set<PriceBook>();

    public DbSet<PriceBookLine> PriceBookLines => Set<PriceBookLine>();

    public DbSet<ProductPriceBookLine> ProductPriceBookLines => Set<ProductPriceBookLine>();

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
    public DbSet<MemberTopupOrder> MemberTopupOrders => Set<MemberTopupOrder>();
    public DbSet<MemberVerificationChallenge> MemberVerificationChallenges => Set<MemberVerificationChallenge>();
    public DbSet<ServiceOrder> ServiceOrders => Set<ServiceOrder>();
    public DbSet<ServiceOrderLine> ServiceOrderLines => Set<ServiceOrderLine>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundLine> RefundLines => Set<RefundLine>();
    public DbSet<CashierShift> CashierShifts => Set<CashierShift>();
    public DbSet<PaymentChannelConfiguration> PaymentChannelConfigurations => Set<PaymentChannelConfiguration>();
    public DbSet<PaymentChannelOrder> PaymentChannelOrders => Set<PaymentChannelOrder>();
    public DbSet<PaymentChannelEvent> PaymentChannelEvents => Set<PaymentChannelEvent>();
    public DbSet<PaymentChannelRefund> PaymentChannelRefunds => Set<PaymentChannelRefund>();
    public DbSet<PaymentChannelReconciliationRun> PaymentChannelReconciliationRuns =>
        Set<PaymentChannelReconciliationRun>();
    public DbSet<PaymentChannelReconciliationItem> PaymentChannelReconciliationItems =>
        Set<PaymentChannelReconciliationItem>();

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
        ConfigureCashier(builder);
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
            entity.Property(x => x.MustChangePassword).HasColumnName("must_change_password");
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

        builder.Entity<Employee>(entity =>
        {
            entity.ToTable("organization_employees");
            ConfigureBase(entity);
            entity.Property(x => x.EmployeeNo).HasColumnName("employee_no").HasMaxLength(32);
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(100);
            entity.Property(x => x.PositionCode).HasColumnName("position_code").HasMaxLength(40);
            entity.Property(x => x.UserId).HasColumnName("user_id");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.HasIndex(x => new { x.TenantId, x.EmployeeNo }).IsUnique();
            entity.HasIndex(x => x.UserId).IsUnique();
            entity.HasOne<ApplicationUser>().WithOne().HasForeignKey<Employee>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<EmployeeStore>(entity =>
        {
            entity.ToTable("organization_employee_stores");
            ConfigureBase(entity);
            entity.Property(x => x.EmployeeId).HasColumnName("employee_id");
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.IsPrimary).HasColumnName("is_primary");
            entity.HasIndex(x => new { x.EmployeeId, x.StoreId }).IsUnique();
            entity.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Store>().WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
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

        builder.Entity<ProductItem>(entity =>
        {
            entity.ToTable("catalog_product_items");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
            entity.Property(x => x.UnitName).HasColumnName("unit_name").HasMaxLength(20);
            entity.Property(x => x.TrackInventory).HasColumnName("track_inventory");
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
            entity.HasMany(x => x.ProductLines).WithOne().HasForeignKey(x => x.PriceBookId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.ProductLines).UsePropertyAccessMode(PropertyAccessMode.Field);
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

        builder.Entity<ProductPriceBookLine>(entity =>
        {
            entity.ToTable("catalog_price_book_product_lines");
            ConfigureBase(entity);
            entity.Property(x => x.PriceBookId).HasColumnName("price_book_id");
            entity.Property(x => x.ProductItemId).HasColumnName("product_item_id");
            entity.Property(x => x.UnitPriceMinor).HasColumnName("unit_price_minor");
            entity.HasIndex(x => new { x.PriceBookId, x.ProductItemId }).IsUnique();
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
            entity.HasIndex(x => new { x.AccountId, x.CommandId }).IsUnique();
        });
        builder.Entity<MemberTopupOrder>(entity =>
        {
            entity.ToTable("member_topup_orders");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.CardId).HasColumnName("card_id");
            entity.Property(x => x.TopupNo).HasColumnName("topup_no").HasMaxLength(40);
            entity.Property(x => x.PrincipalMinor).HasColumnName("principal_minor");
            entity.Property(x => x.BonusMinor).HasColumnName("bonus_minor");
            entity.Property(x => x.ReceivableMinor).HasColumnName("receivable_minor");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
            entity.HasIndex(x => new { x.TenantId, x.TopupNo }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.PaidAtUtc });
            entity.HasIndex(x => new { x.CustomerId, x.PaidAtUtc });
        });
        builder.Entity<MemberVerificationChallenge>(entity =>
        {
            entity.ToTable("member_verification_challenges");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.OrderId).HasColumnName("order_id");
            entity.Property(x => x.AuthorizedAmountMinor).HasColumnName("authorized_amount_minor");
            entity.Property(x => x.CodeSalt).HasColumnName("code_salt");
            entity.Property(x => x.CodeHash).HasColumnName("code_hash");
            entity.Property(x => x.MobileLastFour).HasColumnName("mobile_last_four").HasMaxLength(4);
            entity.Property(x => x.RequestedBy).HasColumnName("requested_by");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.AttemptsRemaining).HasColumnName("attempts_remaining");
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.VerifiedAtUtc).HasColumnName("verified_at_utc");
            entity.Property(x => x.UsedAtUtc).HasColumnName("used_at_utc");
            entity.HasIndex(x => new { x.OrderId, x.Status });
            entity.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
        });
    }

    private static void ConfigureCashier(ModelBuilder builder)
    {
        builder.Entity<ServiceOrder>(entity =>
        {
            entity.ToTable("service_orders");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.VisitId).HasColumnName("visit_id");
            entity.Property(x => x.CustomerId).HasColumnName("customer_id");
            entity.Property(x => x.OrderNo).HasColumnName("order_no").HasMaxLength(40);
            entity.Property(x => x.PriceBookId).HasColumnName("price_book_id");
            entity.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ReferenceAmountMinor).HasColumnName("reference_amount_minor");
            entity.Property(x => x.ReceivableMinor).HasColumnName("receivable_minor");
            entity.Property(x => x.RefundedMinor).HasColumnName("refunded_minor");
            entity.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
            entity.Property(x => x.SettledAtUtc).HasColumnName("settled_at_utc");
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.TenantId, x.OrderNo }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.Status, x.CreatedAtUtc });
        });
        builder.Entity<ServiceOrderLine>(entity =>
        {
            entity.ToTable("service_order_lines");
            ConfigureBase(entity);
            entity.Property(x => x.OrderId).HasColumnName("order_id");
            entity.Property(x => x.ServiceItemId).HasColumnName("service_item_id");
            entity.Property(x => x.ItemCodeSnapshot).HasColumnName("item_code_snapshot").HasMaxLength(40);
            entity.Property(x => x.ItemNameSnapshot).HasColumnName("item_name_snapshot").HasMaxLength(120);
            entity.Property(x => x.Quantity).HasColumnName("quantity");
            entity.Property(x => x.ActualSeconds).HasColumnName("actual_seconds");
            entity.Property(x => x.ReferencePriceMinor).HasColumnName("reference_price_minor");
            entity.Property(x => x.EnteredPriceMinor).HasColumnName("entered_price_minor");
            entity.Property(x => x.ReferenceAmountMinor).HasColumnName("reference_amount_minor");
            entity.Property(x => x.LineAmountMinor).HasColumnName("line_amount_minor");
            entity.Property(x => x.PriceOverrideReason).HasColumnName("price_override_reason").HasMaxLength(500);
            entity.HasIndex(x => new { x.OrderId, x.ServiceItemId }).IsUnique();
        });
        builder.Entity<PaymentMethod>(entity =>
        {
            entity.ToTable("payment_methods");
            ConfigureBase(entity);
            entity.Property(x => x.Code).HasColumnName("code").HasMaxLength(40);
            entity.Property(x => x.Name).HasColumnName("name").HasMaxLength(80);
            entity.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.InternalAccountType).HasColumnName("internal_account_type")
                .HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ChannelProvider).HasColumnName("channel_provider")
                .HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.RequiresOpenShift).HasColumnName("requires_open_shift");
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            entity.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        builder.Entity<CashierShift>(entity =>
        {
            entity.ToTable("cashier_shifts");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.OperatorId).HasColumnName("operator_id");
            entity.Property(x => x.ShiftNo).HasColumnName("shift_no").HasMaxLength(40);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.OpeningCashMinor).HasColumnName("opening_cash_minor");
            entity.Property(x => x.ExpectedCashMinor).HasColumnName("expected_cash_minor");
            entity.Property(x => x.SubmittedCashMinor).HasColumnName("submitted_cash_minor");
            entity.Property(x => x.CashDifferenceMinor).HasColumnName("cash_difference_minor");
            entity.Property(x => x.PendingReconciliationMinor).HasColumnName("pending_reconciliation_minor");
            entity.Property(x => x.HandoverNote).HasColumnName("handover_note").HasMaxLength(500);
            entity.Property(x => x.OpenedAtUtc).HasColumnName("opened_at_utc");
            entity.Property(x => x.SubmittedAtUtc).HasColumnName("submitted_at_utc");
            entity.Property(x => x.ReviewedBy).HasColumnName("reviewed_by");
            entity.Property(x => x.ReviewReason).HasColumnName("review_reason").HasMaxLength(500);
            entity.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc");
            entity.HasIndex(x => new { x.TenantId, x.ShiftNo }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.OperatorId, x.Status });
        });
        builder.Entity<Payment>(entity =>
        {
            entity.ToTable("payments");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.OrderId).HasColumnName("order_id");
            entity.Property(x => x.BusinessType).HasColumnName("business_type").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.BusinessId).HasColumnName("business_id");
            entity.Property(x => x.PaymentNo).HasColumnName("payment_no").HasMaxLength(40);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(x => x.ReceivableMinor).HasColumnName("receivable_minor");
            entity.Property(x => x.PaidMinor).HasColumnName("paid_minor");
            entity.Property(x => x.RefundedMinor).HasColumnName("refunded_minor");
            entity.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
            entity.HasMany(x => x.Allocations).WithOne().HasForeignKey(x => x.PaymentId).OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Allocations).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.TenantId, x.PaymentNo }).IsUnique();
            entity.HasIndex(x => x.OrderId).IsUnique()
                .HasFilter("order_id IS NOT NULL AND status IN ('Processing','Paid','PartiallyRefunded','Refunded')");
            entity.HasIndex(x => new { x.TenantId, x.BusinessType, x.BusinessId }).IsUnique()
                .HasFilter("status IN ('Processing','Paid','PartiallyRefunded','Refunded')");
        });
        builder.Entity<PaymentAllocation>(entity =>
        {
            entity.ToTable("payment_allocations");
            ConfigureBase(entity);
            entity.Property(x => x.PaymentId).HasColumnName("payment_id");
            entity.Property(x => x.MethodId).HasColumnName("method_id");
            entity.Property(x => x.MethodCodeSnapshot).HasColumnName("method_code_snapshot").HasMaxLength(40);
            entity.Property(x => x.MethodNameSnapshot).HasColumnName("method_name_snapshot").HasMaxLength(80);
            entity.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.AmountMinor).HasColumnName("amount_minor");
            entity.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(128);
            entity.Property(x => x.ShiftId).HasColumnName("shift_id");
            entity.Property(x => x.MemberAccountId).HasColumnName("member_account_id");
            entity.Property(x => x.ChannelProvider).HasColumnName("channel_provider")
                .HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ConfirmationStatus).HasColumnName("confirmation_status").HasConversion<string>().HasMaxLength(48);
            entity.Property(x => x.ReconciliationStatus).HasColumnName("reconciliation_status").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.ConfirmedAtUtc).HasColumnName("confirmed_at_utc");
            entity.HasIndex(x => x.PaymentId);
            entity.HasIndex(x => x.ShiftId);
            entity.HasIndex(x => x.MemberAccountId);
            entity.HasOne<MemberAccount>().WithMany().HasForeignKey(x => x.MemberAccountId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Refund>(entity =>
        {
            entity.ToTable("payment_refunds");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.PaymentId).HasColumnName("payment_id");
            entity.Property(x => x.RefundNo).HasColumnName("refund_no").HasMaxLength(40);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.AmountMinor).HasColumnName("amount_minor");
            entity.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
            entity.Property(x => x.RequestedBy).HasColumnName("requested_by");
            entity.Property(x => x.RequestedAtUtc).HasColumnName("requested_at_utc");
            entity.Property(x => x.ApprovedBy).HasColumnName("approved_by");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500);
            entity.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RefundId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.HasIndex(x => new { x.TenantId, x.RefundNo }).IsUnique();
            entity.HasIndex(x => new { x.PaymentId, x.Status });
        });
        builder.Entity<RefundLine>(entity =>
        {
            entity.ToTable("payment_refund_lines");
            ConfigureBase(entity);
            entity.Property(x => x.RefundId).HasColumnName("refund_id");
            entity.Property(x => x.OriginalAllocationId).HasColumnName("original_allocation_id");
            entity.Property(x => x.AmountMinor).HasColumnName("amount_minor");
            entity.Property(x => x.Category).HasColumnName("category").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.MemberAccountId).HasColumnName("member_account_id");
            entity.Property(x => x.Route).HasColumnName("route").HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.CashShiftId).HasColumnName("cash_shift_id");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.HasIndex(x => new { x.RefundId, x.OriginalAllocationId }).IsUnique();
            entity.HasIndex(x => x.OriginalAllocationId);
            entity.HasIndex(x => x.CashShiftId);
        });
        builder.Entity<PaymentChannelConfiguration>(entity =>
        {
            entity.ToTable("payment_channel_configurations");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.Environment).HasColumnName("environment").HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80);
            entity.Property(x => x.CredentialProfile).HasColumnName("credential_profile").HasMaxLength(40);
            entity.Property(x => x.IsEnabled).HasColumnName("is_enabled");
            entity.HasIndex(x => new { x.TenantId, x.StoreId, x.Provider }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.IsEnabled, x.Provider });
        });
        builder.Entity<PaymentChannelOrder>(entity =>
        {
            entity.ToTable("payment_channel_orders");
            ConfigureBase(entity);
            entity.Property(x => x.ConfigurationId).HasColumnName("configuration_id");
            entity.Property(x => x.PaymentAllocationId).HasColumnName("payment_allocation_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.OutTradeNo).HasColumnName("out_trade_no").HasMaxLength(64);
            entity.Property(x => x.AttemptNo).HasColumnName("attempt_no");
            entity.Property(x => x.AmountMinor).HasColumnName("amount_minor");
            entity.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(120);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.QrPayload).HasColumnName("qr_payload").HasMaxLength(2048);
            entity.Property(x => x.ProviderTradeNo).HasColumnName("provider_trade_no").HasMaxLength(128);
            entity.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(80);
            entity.Property(x => x.ExpiresAtUtc).HasColumnName("expires_at_utc");
            entity.Property(x => x.PaidAtUtc).HasColumnName("paid_at_utc");
            entity.Property(x => x.ClosedAtUtc).HasColumnName("closed_at_utc");
            entity.Property(x => x.LastQueriedAtUtc).HasColumnName("last_queried_at_utc");
            entity.HasIndex(x => new { x.TenantId, x.Provider, x.OutTradeNo }).IsUnique();
            entity.HasIndex(x => new { x.PaymentAllocationId, x.AttemptNo }).IsUnique();
            entity.HasIndex(x => new { x.ConfigurationId, x.Status, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.Provider, x.ProviderTradeNo });
            entity.HasOne<PaymentChannelConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => x.PaymentAllocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentChannelEvent>(entity =>
        {
            entity.ToTable("payment_channel_events");
            ConfigureBase(entity);
            entity.Property(x => x.ConfigurationId).HasColumnName("configuration_id");
            entity.Property(x => x.ChannelOrderId).HasColumnName("channel_order_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(128);
            entity.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(80);
            entity.Property(x => x.PayloadSha256).HasColumnName("payload_sha256");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ReceivedAtUtc).HasColumnName("received_at_utc");
            entity.Property(x => x.ProcessedAtUtc).HasColumnName("processed_at_utc");
            entity.Property(x => x.ErrorCode).HasColumnName("error_code").HasMaxLength(80);
            entity.HasIndex(x => new { x.ConfigurationId, x.ProviderEventId }).IsUnique();
            entity.HasIndex(x => new { x.ChannelOrderId, x.ReceivedAtUtc });
            entity.HasOne<PaymentChannelConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentChannelOrder>().WithMany().HasForeignKey(x => x.ChannelOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentChannelRefund>(entity =>
        {
            entity.ToTable("payment_channel_refunds");
            ConfigureBase(entity);
            entity.Property(x => x.ConfigurationId).HasColumnName("configuration_id");
            entity.Property(x => x.RefundId).HasColumnName("refund_id");
            entity.Property(x => x.OriginalChannelOrderId).HasColumnName("original_channel_order_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.OutRefundNo).HasColumnName("out_refund_no").HasMaxLength(64);
            entity.Property(x => x.OutTradeNo).HasColumnName("out_trade_no").HasMaxLength(64);
            entity.Property(x => x.ProviderTradeNo).HasColumnName("provider_trade_no").HasMaxLength(128);
            entity.Property(x => x.ProviderRefundNo).HasColumnName("provider_refund_no").HasMaxLength(128);
            entity.Property(x => x.AmountMinor).HasColumnName("amount_minor");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.ReconciliationStatus).HasColumnName("reconciliation_status")
                .HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(80);
            entity.Property(x => x.LastQueriedAtUtc).HasColumnName("last_queried_at_utc");
            entity.Property(x => x.SucceededAtUtc).HasColumnName("succeeded_at_utc");
            entity.HasIndex(x => x.RefundId).IsUnique();
            entity.HasIndex(x => new { x.ConfigurationId, x.OutRefundNo }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            entity.HasOne<PaymentChannelConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Refund>().WithMany().HasForeignKey(x => x.RefundId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentChannelOrder>().WithMany().HasForeignKey(x => x.OriginalChannelOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentChannelReconciliationRun>(entity =>
        {
            entity.ToTable("payment_channel_reconciliation_runs");
            ConfigureBase(entity);
            entity.Property(x => x.StoreId).HasColumnName("store_id");
            entity.Property(x => x.ConfigurationId).HasColumnName("configuration_id");
            entity.Property(x => x.Provider).HasColumnName("provider").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.BusinessDate).HasColumnName("business_date");
            entity.Property(x => x.AttemptNo).HasColumnName("attempt_no");
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.StartedBy).HasColumnName("started_by");
            entity.Property(x => x.StartedAtUtc).HasColumnName("started_at_utc");
            entity.Property(x => x.CompletedAtUtc).HasColumnName("completed_at_utc");
            entity.Property(x => x.ChannelEntryCount).HasColumnName("channel_entry_count");
            entity.Property(x => x.MatchedCount).HasColumnName("matched_count");
            entity.Property(x => x.DifferenceCount).HasColumnName("difference_count");
            entity.Property(x => x.SourceSha256).HasColumnName("source_sha256");
            entity.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(80);
            entity.HasIndex(x => new { x.ConfigurationId, x.BusinessDate, x.AttemptNo }).IsUnique();
            entity.HasIndex(x => new { x.StoreId, x.BusinessDate, x.StartedAtUtc });
            entity.HasOne<PaymentChannelConfiguration>().WithMany().HasForeignKey(x => x.ConfigurationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Erp.Domain.Organization.Store>().WithMany().HasForeignKey(x => x.StoreId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.StartedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PaymentChannelReconciliationItem>(entity =>
        {
            entity.ToTable("payment_channel_reconciliation_items");
            ConfigureBase(entity);
            entity.Property(x => x.RunId).HasColumnName("run_id");
            entity.Property(x => x.ItemType).HasColumnName("item_type").HasConversion<string>().HasMaxLength(16);
            entity.Property(x => x.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(24);
            entity.Property(x => x.MatchKey).HasColumnName("match_key").HasMaxLength(160);
            entity.Property(x => x.OutTradeNo).HasColumnName("out_trade_no").HasMaxLength(64);
            entity.Property(x => x.OutRefundNo).HasColumnName("out_refund_no").HasMaxLength(64);
            entity.Property(x => x.ProviderTradeNo).HasColumnName("provider_trade_no").HasMaxLength(128);
            entity.Property(x => x.PaymentAllocationId).HasColumnName("payment_allocation_id");
            entity.Property(x => x.ChannelRefundId).HasColumnName("channel_refund_id");
            entity.Property(x => x.LocalAmountMinor).HasColumnName("local_amount_minor");
            entity.Property(x => x.ChannelAmountMinor).HasColumnName("channel_amount_minor");
            entity.Property(x => x.ChannelFeeMinor).HasColumnName("channel_fee_minor");
            entity.Property(x => x.LocalStatus).HasColumnName("local_status").HasMaxLength(40);
            entity.Property(x => x.ChannelStatus).HasColumnName("channel_status").HasMaxLength(80);
            entity.Property(x => x.ResolvedBy).HasColumnName("resolved_by");
            entity.Property(x => x.ResolvedAtUtc).HasColumnName("resolved_at_utc");
            entity.Property(x => x.ResolutionReason).HasColumnName("resolution_reason").HasMaxLength(500);
            entity.HasIndex(x => new { x.RunId, x.MatchKey }).IsUnique();
            entity.HasIndex(x => new { x.RunId, x.Status });
            entity.HasIndex(x => x.PaymentAllocationId);
            entity.HasIndex(x => x.ChannelRefundId);
            entity.HasOne<PaymentChannelReconciliationRun>().WithMany().HasForeignKey(x => x.RunId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentAllocation>().WithMany().HasForeignKey(x => x.PaymentAllocationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PaymentChannelRefund>().WithMany().HasForeignKey(x => x.ChannelRefundId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResolvedBy)
                .OnDelete(DeleteBehavior.Restrict);
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
