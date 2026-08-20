using System.Text.RegularExpressions;
using Erp.Application.Security;
using Erp.Infrastructure.Customers;

namespace Erp.Api.IntegrationTests;

public sealed partial class RepositoryArtifactIntegrationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string[] RealtimeSearchPages =
        ["CustomersPage.tsx", "EmployeesPage.tsx", "ServiceItemsPage.tsx", "ProductsPage.tsx"];

    [Fact]
    public void RolePermissionCatalogSeedsEveryDefaultGrantAndEnforcesDatabaseAuthorization()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608190030__role_permission_catalog.sql"));
        var handler = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Security", "PermissionAuthorization.cs"));
        var identity = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Identity", "IdentityService.cs"));

        foreach (var permission in SystemPermissions.ForRole(SystemRoles.Owner))
            Assert.Contains($"('{permission}')", migration, StringComparison.Ordinal);
        foreach (var role in new[]
                 {
                     SystemRoles.StoreManager, SystemRoles.FrontDesk, SystemRoles.Cashier,
                     SystemRoles.Technician,
                 })
        foreach (var permission in SystemPermissions.ForRole(role))
            Assert.Contains($"('{role}', '{permission}')", migration, StringComparison.Ordinal);

        Assert.Contains("join grant in db.RoleActionGrants", handler, StringComparison.Ordinal);
        Assert.Contains("grant.TenantId == user.TenantId", handler, StringComparison.Ordinal);
        Assert.Contains("permissions = await dbContext.RoleActionGrants", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticBusinessCodesUseAForwardOnlySequenceMigration()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608200031__automatic_business_code_sequences.sql"));
        var generator = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Organization", "BusinessCodeGenerator.cs"));

        Assert.Contains("CREATE TABLE platform_code_sequences", migration, StringComparison.Ordinal);
        Assert.Contains("ON CONFLICT (sequence_name, scope_key)", generator, StringComparison.Ordinal);
        Assert.Contains("current_value = platform_code_sequences.current_value + 1", generator,
            StringComparison.Ordinal);
        Assert.DoesNotContain("SELECT MAX(code)", generator, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegacyImportIsB01OnlyIdempotentAndKeepsFinancialSnapshotsNonSpendable()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608200032__legacy_migration_control_and_snapshots.sql"));
        var importer = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "LegacyMigration", "LegacyImportService.cs"));
        var tool = File.ReadAllText(Path.Combine(RepositoryRoot, "tools", "Erp.LegacyMigration",
            "LegacyImportCli.cs"));

        Assert.Contains("UNIQUE (tenant_id, source_entity, source_id)", migration, StringComparison.Ordinal);
        Assert.Contains("CHECK (is_spendable = false)", migration, StringComparison.Ordinal);
        Assert.Contains("reject_legacy_append_only_change", migration, StringComparison.Ordinal);
        Assert.Contains("dataset.TenantCode is not \"B01\"", importer, StringComparison.Ordinal);
        Assert.Contains("new LegacyImportCommand(dataset, !options.Apply)", tool, StringComparison.Ordinal);
        Assert.DoesNotContain("MapPost", importer, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseMigrationsAreUniquelyVersionedAndNonDestructive()
    {
        var migrationDirectory = Path.Combine(RepositoryRoot, "db", "migrations");
        var migrations = Directory.GetFiles(migrationDirectory, "V*.sql").Order().ToList();

        Assert.NotEmpty(migrations);
        var versions = migrations.Select(path => MigrationNamePattern().Match(Path.GetFileName(path)))
            .Select(match => match.Success ? match.Groups[1].Value : string.Empty).ToList();
        Assert.DoesNotContain(string.Empty, versions);
        Assert.Equal(versions.Count, versions.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(versions.Order(StringComparer.Ordinal), versions);
        Assert.All(migrations, path =>
        {
            var sql = File.ReadAllText(path);
            Assert.DoesNotContain("DROP TABLE", sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("TRUNCATE", sql, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void MergedUserManualReferencesExistingScreenshots()
    {
        var manualPath = Path.Combine(RepositoryRoot, "docs", "user-manual", "ERP-V1-user-manual.md");
        var manual = File.ReadAllText(manualPath);
        var imagePaths = MarkdownImagePattern().Matches(manual).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(imagePaths);
        Assert.All(imagePaths, relativePath =>
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(manualPath)!, relativePath)),
                $"用户手册截图不存在：{relativePath}"));
    }

    [Fact]
    public void MemberTopupMigrationBackfillsExistingPaymentsAndKeepsServiceRevenueSeparated()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180008__member_topups_and_generalized_payments.sql"));
        var report = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Reports", "ReportService.cs"));

        Assert.Contains("business_type = 'ServiceOrder'", migration, StringComparison.Ordinal);
        Assert.Contains("receivable_minor = principal_minor", migration, StringComparison.Ordinal);
        Assert.Contains("PaymentBusinessType.ServiceOrder", report, StringComparison.Ordinal);
    }

    [Fact]
    public void MemberPaymentMigrationRequiresTypedAccountAndBoundOneTimeChallenge()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180009__member_account_payments_and_verification.sql"));
        var paymentService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api",
            "Erp.Infrastructure", "Cashier", "PaymentService.cs"));

        Assert.Contains("internal_account_type", migration, StringComparison.Ordinal);
        Assert.Contains("member_account_id uuid REFERENCES member_accounts(id) ON DELETE RESTRICT", migration,
            StringComparison.Ordinal);
        Assert.Contains("authorized_amount_minor BETWEEN 50000", migration, StringComparison.Ordinal);
        Assert.Contains("octet_length(code_hash) = 32", migration, StringComparison.Ordinal);
        Assert.Contains("verificationChallenge.Consume(order.Id, order.CustomerId.Value, memberAmountMinor",
            paymentService, StringComparison.Ordinal);
        Assert.Contains("account.Debit(\"ServiceOrder\"", paymentService, StringComparison.Ordinal);
    }

    [Fact]
    public void RefundMigrationPreservesOriginalRecordsAndTracksBoundReverseLines()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180010__service_payment_refunds.sql"));
        var refundService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api",
            "Erp.Infrastructure", "Cashier", "RefundService.cs"));

        Assert.Contains("REFERENCES payment_allocations(id) ON DELETE RESTRICT", migration,
            StringComparison.Ordinal);
        Assert.Contains("refunded_minor BETWEEN 0 AND paid_minor", migration, StringComparison.Ordinal);
        Assert.Contains("status = 'PendingApproval'", migration, StringComparison.Ordinal);
        Assert.Contains("account.Credit(\"PaymentRefund\"", refundService, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove(", refundService, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentChannelFoundationStoresReferencesAndDigestsButNoSecretMaterial()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180011__payment_channel_foundation.sql"));
        var resolver = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "PaymentChannelCredentials.cs"));

        Assert.Contains("credential_profile varchar(40) NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("payload_sha256 bytea NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("octet_length(payload_sha256) = 32", migration, StringComparison.Ordinal);
        Assert.Contains("uq_payment_channel_order_active_allocation", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("private_key varchar", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api_v3_key", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PaymentChannels:Profiles:{profile}", resolver, StringComparison.Ordinal);
        Assert.Contains("File.Exists(path)", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncChannelMigrationAndServiceRequireVerifiedResultsBeforeSettlement()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180012__async_channel_payments.sql"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "PaymentChannelPaymentService.cs"));

        Assert.Contains("ChannelPending", migration, StringComparison.Ordinal);
        Assert.Contains("confirmed_at_utc DROP NOT NULL", migration, StringComparison.Ordinal);
        Assert.Contains("status IN ('Processing', 'Paid', 'PartiallyRefunded', 'Refunded')", migration,
            StringComparison.Ordinal);
        Assert.Contains("VerifyNotification", service, StringComparison.Ordinal);
        Assert.Contains("ApplyPaidInsideTransactionAsync", service, StringComparison.Ordinal);
        Assert.Contains("CHANNEL_AMOUNT_OR_TRADE_CONFLICT", service, StringComparison.Ordinal);
        Assert.Contains("CHANNEL_LATE_PAYMENT_REQUIRES_REVERSAL", service, StringComparison.Ordinal);
        Assert.DoesNotContain("PaymentStatus.Paid", service[..service.IndexOf("CreateQrAsync", StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelRefundMigrationKeepsLocalLedgerPendingUntilProviderSuccess()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180013__channel_refunds.sql"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "RefundService.cs"));

        Assert.Contains("status IN ('PendingApproval', 'Processing', 'Completed', 'Rejected')", migration,
            StringComparison.Ordinal);
        Assert.Contains("CONSTRAINT uq_payment_channel_refunds_refund UNIQUE (refund_id)", migration,
            StringComparison.Ordinal);
        Assert.Contains("OriginalChannel", migration, StringComparison.Ordinal);
        Assert.Contains("currentChannel.MarkSucceeded", service, StringComparison.Ordinal);
        Assert.True(service.IndexOf("currentChannel.MarkSucceeded", StringComparison.Ordinal) <
            service.IndexOf("currentPayment.ApplyRefund", StringComparison.Ordinal));
        Assert.Contains("currentRefund.CompleteChannel", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ChannelReconciliationStoresDigestAndDifferencesButNeverRawBillOrAutomaticCorrections()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180014__channel_bill_reconciliation.sql"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "PaymentChannelReconciliationService.cs"));

        Assert.Contains("source_sha256 bytea", migration, StringComparison.Ordinal);
        Assert.Contains("octet_length(source_sha256) = 32", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("raw_bill", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PaymentChannelReconciliationItemStatus.AmountMismatch", service,
            StringComparison.Ordinal);
        Assert.Contains("PaymentChannelReconciliationItemStatus.ChannelOnly", service,
            StringComparison.Ordinal);
        Assert.Contains("item.Resolve", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyRefund", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfirmChannelAllocation", service, StringComparison.Ordinal);
    }

    [Fact]
    public void InventoryMigrationSeparatesReservationIssueReturnAndFinancialRefund()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180015__product_sales_and_inventory.sql"));
        var inventory = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Inventory", "InventoryPostingService.cs"));

        Assert.Contains("CREATE TABLE inventory_reservations", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE inventory_movements", migration, StringComparison.Ordinal);
        Assert.Contains("inventory movements are append-only", migration, StringComparison.Ordinal);
        Assert.Contains("command_id uuid NOT NULL UNIQUE", migration, StringComparison.Ordinal);
        Assert.Contains("ReserveOrderAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("ConsumeOrderAsync", inventory, StringComparison.Ordinal);
        Assert.Contains("SalesReturn", inventory, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyRefund", inventory, StringComparison.Ordinal);
    }

    [Fact]
    public void ImageAndServiceArchiveMigrationUsesPrivateBoundedAppendOnlyStorage()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180016__product_images_and_customer_service_records.sql"));
        var storage = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Files", "SecureFileStorage.cs"));
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api",
            "Endpoints", "CustomerEndpoints.cs"));

        Assert.Contains("size_bytes BETWEEN 1 AND 5242880", migration, StringComparison.Ordinal);
        Assert.Contains("octet_length(sha256) = 32", migration, StringComparison.Ordinal);
        Assert.Contains("service archive records are append-only", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("file_content bytea", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("protector.Protect(content)", storage, StringComparison.Ordinal);
        Assert.Contains("DetectContentType(content)", storage, StringComparison.Ordinal);
        Assert.Contains("Path.GetFullPath", storage, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.ServiceRecordManage)", endpoints,
            StringComparison.Ordinal);
    }

    [Fact]
    public void FacilityConfigurationKeepsReferencePriceOptionalAndOutsideCashierPricing()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180017__facility_configuration_management.sql"));
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api",
            "Endpoints", "FacilityEndpoints.cs"));
        var cashier = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "CashierService.cs"));

        Assert.Contains("reference_price_minor bigint", migration, StringComparison.Ordinal);
        Assert.Contains("reference_price_minor IS NULL", migration, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.FacilityConfigure)", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.FacilityConfigureAllStores)", endpoints,
            StringComparison.Ordinal);
        Assert.DoesNotContain("facility.ReferencePriceMinor", cashier, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitRecognitionContextIsOptionalScopedAndNeverAutomaticPricing()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180018__recognizable_visit_context.sql"));
        var facilityService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Facilities", "FacilityService.cs"));
        var cashierPage = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "web", "src", "pages",
            "CashierPage.tsx"));

        Assert.Contains("planned_service_item_id uuid", migration, StringComparison.Ordinal);
        Assert.Contains("ON DELETE RESTRICT", migration, StringComparison.Ordinal);
        Assert.Contains("never creates a charge", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("x.HomeStoreId == command.StoreId", facilityService, StringComparison.Ordinal);
        Assert.Contains("x.TenantId == tenantId && x.Status == CustomerStatus.Active", facilityService,
            StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerPrivacyService.MaskName", facilityService, StringComparison.Ordinal);
        Assert.Contains("ToDictionaryAsync(x => x.Id, x => x.Name", facilityService, StringComparison.Ordinal);
        Assert.Contains("带入预计服务", cashierPage, StringComparison.Ordinal);
        Assert.Contains("预计服务都不会自动形成费用", cashierPage, StringComparison.Ordinal);
    }

    [Fact]
    public void CustomerSearchUsesFullMobileHashWhileResponsesKeepOnlyMiddleFourMasked()
    {
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Customers", "CustomerService.cs"));
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api", "Endpoints",
            "CustomerEndpoints.cs"));

        Assert.Equal("138****1234", CustomerPrivacyService.MaskMobile("13812341234"));
        Assert.Contains("privacy.Hash(digits)", service, StringComparison.Ordinal);
        Assert.Contains("x.MobileLookupHash == hash", service, StringComparison.Ordinal);
        Assert.Contains("privacy.MaskProtectedMobile", service, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomerPrivacyService.MaskName", service, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/search\"", endpoints, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet(\"\",", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequireRateLimiting(\"customer-search\")", endpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogManagementSupportsSearchEditSafeDeleteAndOwnerAuthorization()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api", "Endpoints",
            "CatalogEndpoints.cs"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Catalog", "CatalogService.cs"));
        var servicePage = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "web", "src", "pages",
            "ServiceItemsPage.tsx"));
        var productPage = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "web", "src", "pages",
            "ProductsPage.tsx"));

        Assert.Contains("MapPut(\"/service-items/{id:guid}\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/service-items/{id:guid}\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapPut(\"/products/{id:guid}\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("MapDelete(\"/products/{id:guid}\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.CatalogWrite)", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("x.Code.Contains(normalizedQuery) || x.Name.Contains(normalizedQuery)", service,
            StringComparison.Ordinal);
        Assert.Contains("RESOURCE_IN_USE", service, StringComparison.Ordinal);
        Assert.Contains("ProductHasInventoryHistoryAsync", service, StringComparison.Ordinal);
        Assert.Contains("expectedVersion", servicePage, StringComparison.Ordinal);
        Assert.Contains("永久删除", servicePage, StringComparison.Ordinal);
        Assert.Contains("expectedVersion", productPage, StringComparison.Ordinal);
        Assert.Contains("已有业务记录的产品请停用", productPage, StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceCommissionMigrationUsesOwnerRulesEmployeeSnapshotsAndRefundDeductions()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180019__service_staff_commission_snapshots.sql"));
        var catalogEndpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api",
            "Endpoints", "CatalogEndpoints.cs"));
        var cashierService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "CashierService.cs"));
        var reportService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Reports", "ReportService.cs"));

        Assert.Contains("commission_rate_basis_points BETWEEN 1 AND 10000", migration, StringComparison.Ordinal);
        Assert.Contains("commission_amount_minor = commission_fixed_minor * quantity", migration,
            StringComparison.Ordinal);
        Assert.Contains("REFERENCES organization_employees(id) ON DELETE RESTRICT", migration,
            StringComparison.Ordinal);
        Assert.Contains("Immutable gross commission snapshot", migration, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.CatalogWrite)", catalogEndpoints,
            StringComparison.Ordinal);
        Assert.Contains("employee.Status == EmployeeStatus.Active", cashierService, StringComparison.Ordinal);
        Assert.Contains("assignment.StoreId == command.StoreId", cashierService, StringComparison.Ordinal);
        Assert.Contains("AllocateRefundDeduction", reportService, StringComparison.Ordinal);
    }

    [Fact]
    public void PriceOverrideApprovalIsVersionedServerAuthorizedAndBlocksPaymentUntilApproved()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180020__price_override_policy_and_approval.sql"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Cashier", "CashierService.cs"));
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api", "Endpoints",
            "CashierEndpoints.cs"));
        var order = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Domain", "Cashier",
            "ServiceOrder.cs"));

        Assert.Contains("CREATE TABLE price_override_policies", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE price_override_approvals", migration, StringComparison.Ordinal);
        Assert.Contains("ck_price_override_approvals_no_self_review", migration, StringComparison.Ordinal);
        Assert.Contains("manager_line_discount_basis_points BETWEEN 0 AND 10000", migration,
            StringComparison.Ordinal);
        Assert.Contains("current.Roles", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.CashierApprovePrice)", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("ResolvePriceRole(command.OperatorRoles)", service, StringComparison.Ordinal);
        Assert.Contains("service_order.price.approval_requested", service, StringComparison.Ordinal);
        Assert.Contains("PRICE_APPROVAL_REQUIRED", order, StringComparison.Ordinal);
        Assert.Contains("PRICE_APPROVAL_SELF_REVIEW_FORBIDDEN", File.ReadAllText(Path.Combine(RepositoryRoot,
            "apps", "api", "Erp.Domain", "Cashier", "PriceOverrideApproval.cs")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SensitiveCustomerAccessIsPurposeBoundAuditedAndCsvFormulaSafe()
    {
        var endpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api", "Endpoints",
            "CustomerEndpoints.cs"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Customers", "CustomerService.cs"));
        var bytes = CustomerExportFormatter.ToCsv([
            new CustomerExportRow("=HYPERLINK(\"https://invalid.example\")", "138****1234", "Active", 1,
                new DateTimeOffset(2026, 8, 18, 8, 0, 0, TimeSpan.FromHours(8))),
        ]);
        var csv = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Contains("\"'=HYPERLINK(\"\"https://invalid.example\"\")\"", csv, StringComparison.Ordinal);
        Assert.Contains("138****1234", csv, StringComparison.Ordinal);
        Assert.Contains("MapPost(\"/{customerId:guid}/mobile/reveal\"", endpoints, StringComparison.Ordinal);
        Assert.Contains("RequireAuthorization(SystemPermissions.CustomerExport)", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("current.Permissions.Contains(SystemPermissions.CustomerExportFullMobile)", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("includeFinancialDetails", endpoints, StringComparison.Ordinal);
        Assert.Contains("customer.mobile.reveal", service, StringComparison.Ordinal);
        Assert.Contains("customer.export", service, StringComparison.Ordinal);
        Assert.Contains("includesFullMobile", service, StringComparison.Ordinal);
        Assert.Contains("includeFinancialDetails", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MobileCiphertext =", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxReleaseAutomationIsLockedIntegrityCheckedAndFailClosed()
    {
        var migrations = Directory.GetFiles(Path.Combine(RepositoryRoot, "db", "migrations"), "V*.sql")
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        var latestVersion = MigrationNamePattern().Match(migrations[^1]!).Groups[1].Value;
        var readiness = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Persistence", "DatabaseReadinessService.cs"));
        var program = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api", "Program.cs"));
        var linuxRoot = Path.Combine(RepositoryRoot, "deploy", "linux");
        var build = File.ReadAllText(Path.Combine(linuxRoot, "Build-Release.sh"));
        var common = File.ReadAllText(Path.Combine(linuxRoot, "common.sh"));
        var deploy = File.ReadAllText(Path.Combine(linuxRoot, "Deploy-Release.sh"));
        var rollback = File.ReadAllText(Path.Combine(linuxRoot, "rollback.sh"));
        var backup = File.ReadAllText(Path.Combine(linuxRoot, "backup.sh"));
        var restore = File.ReadAllText(Path.Combine(linuxRoot, "verify-backup.sh"));
        var initialize = File.ReadAllText(Path.Combine(linuxRoot, "Initialize-Host.sh"));
        var workflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "release.yml"));

        Assert.Contains($"RequiredSchemaVersion = \"{latestVersion}\"", readiness, StringComparison.Ordinal);
        Assert.Contains("schema_version=$(find", build, StringComparison.Ordinal);
        Assert.Contains("\"schema\": {\"min\": schema, \"max\": schema}", build, StringComparison.Ordinal);
        Assert.Contains("linux-x64-framework-dependent", build, StringComparison.Ordinal);
        Assert.Contains("VITE_APP_VERSION=\"$version\" VITE_APP_ENVIRONMENT=Production", build,
            StringComparison.Ordinal);
        Assert.Contains("MapGet(\"/health/ready\"", program, StringComparison.Ordinal);
        Assert.Contains("UseStaticFiles", program, StringComparison.Ordinal);
        Assert.Contains("MapFallbackToFile(\"index.html\")", program, StringComparison.Ordinal);
        Assert.Contains("/api/{**path}", program, StringComparison.Ordinal);
        Assert.Contains("sha256_file", common, StringComparison.Ordinal);
        Assert.Contains("safe_absolute_directory", common, StringComparison.Ordinal);
        Assert.Contains("archive path escapes root", deploy, StringComparison.Ordinal);
        Assert.Contains("manifest mismatch", deploy, StringComparison.Ordinal);
        Assert.Contains("expected_hash", deploy, StringComparison.Ordinal);
        Assert.Contains("-baselineOnMigrate=false", deploy, StringComparison.Ordinal);
        Assert.Contains("-cleanDisabled=true", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain(" repair", deploy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("flyway clean", deploy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/usr/local/sbin/erp-backup", deploy, StringComparison.Ordinal);
        Assert.Contains("previous_upstream", deploy, StringComparison.Ordinal);
        Assert.Contains("current_schema", rollback, StringComparison.Ordinal);
        Assert.Contains("schema_min", rollback, StringComparison.Ordinal);
        Assert.Contains("previous_upstream", rollback, StringComparison.Ordinal);
        Assert.Contains("--no-restore", build, StringComparison.Ordinal);
        Assert.Contains("age --recipient", backup, StringComparison.Ordinal);
        Assert.Contains("schemaVersion", backup, StringComparison.Ordinal);
        Assert.Contains("erp_restore_verify_", restore, StringComparison.Ordinal);
        Assert.Contains("隔离恢复目标已经存在，拒绝覆盖", restore, StringComparison.Ordinal);
        Assert.Contains("backup path escapes root", restore, StringComparison.Ordinal);
        Assert.Contains("PasswordAuthentication no", initialize, StringComparison.Ordinal);
        Assert.Contains("listen_addresses = '127.0.0.1,::1'", initialize, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS=http://127.0.0.1", initialize, StringComparison.Ordinal);
        Assert.Contains("ufw default deny incoming", initialize, StringComparison.Ordinal);
        Assert.Contains("ubuntu && ${VERSION_ID:-} == 24.04", initialize, StringComparison.Ordinal);
        Assert.Contains("dotnet restore ERP.slnx --locked-mode", workflow, StringComparison.Ordinal);
        Assert.Contains("npm audit --audit-level=moderate", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: ubuntu-24.04", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);

        var projects = Directory.GetFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .ToList();
        Assert.All(projects, project => Assert.True(
            File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "packages.lock.json")),
            $"缺少 NuGet 锁文件：{Path.GetRelativePath(RepositoryRoot, project)}"));
    }

    [Fact]
    public void RealtimeSearchIsDebouncedCancelableAndBackedBySubstringIndexes()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180021__realtime_search_indexes.sql"));
        var hook = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "web", "src", "hooks",
            "useDebouncedValue.ts"));
        var employeeEndpoint = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api",
            "Endpoints", "EmployeeEndpoints.cs"));
        var employeeService = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Identity", "EmployeeService.cs"));
        var pages = RealtimeSearchPages
            .Select(name => File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "web", "src", "pages", name)))
            .ToList();

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS pg_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("USING gin (name gin_trgm_ops)", migration, StringComparison.Ordinal);
        Assert.Contains("ix_organization_employees_name_trgm", migration, StringComparison.Ordinal);
        Assert.Contains("setTimeout", hook, StringComparison.Ordinal);
        Assert.Contains("clearTimeout", hook, StringComparison.Ordinal);
        Assert.Contains("query?.Trim().Length > 100", employeeEndpoint, StringComparison.Ordinal);
        Assert.Contains("employee.DisplayName.Contains(term)", employeeService, StringComparison.Ordinal);
        Assert.Contains("user.UserName.Contains(term)", employeeService, StringComparison.Ordinal);
        Assert.Contains("store.Name.Contains(term)", employeeService, StringComparison.Ordinal);
        Assert.All(pages, page =>
        {
            Assert.Contains("useDebouncedValue", page, StringComparison.Ordinal);
            Assert.Contains("({ signal })", page, StringComparison.Ordinal);
            Assert.Contains("自动加载，无需点击查询", page, StringComparison.Ordinal);
            Assert.DoesNotContain(">查询</Button>", page, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void SupplyChainMigrationKeepsPostedFactsAndLotAllocationsImmutable()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608180028__supply_chain_advanced_inventory.sql"));
        var service = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Infrastructure",
            "Inventory", "SupplyChainService.cs"));

        Assert.Contains("CREATE TABLE suppliers", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE purchase_receipts", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE inventory_lots", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE stocktakes", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE inventory_transfers", migration, StringComparison.Ordinal);
        Assert.Contains("trg_inventory_lot_allocations_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("trg_purchase_receipts_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("trg_stocktake_lines_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("trg_inventory_transfer_lots_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("IsolationLevel.Serializable", service, StringComparison.Ordinal);
        Assert.Contains("INVENTORY_LOT_INSUFFICIENT", service, StringComparison.Ordinal);
    }

    [Fact]
    public void PlatformControlPlaneUsesSeparateIdentityAndImmutableSecurityEvents()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot, "db", "migrations",
            "V202608190029__platform_control_plane_and_login_security.sql"));
        var dependencyInjection = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api",
            "Erp.Infrastructure", "DependencyInjection.cs"));
        var platformEndpoints = File.ReadAllText(Path.Combine(RepositoryRoot, "apps", "api", "Erp.Api",
            "Endpoints", "PlatformEndpoints.cs"));

        Assert.Contains("CREATE TABLE platform_admin_users", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE merchant_registration_applications", migration, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE login_security_events", migration, StringComparison.Ordinal);
        Assert.Contains("trg_login_security_events_immutable", migration, StringComparison.Ordinal);
        Assert.Contains("trg_platform_audit_events_immutable", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("password varchar", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Erp.Platform.Session", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("PlatformAuthentication.Policy", platformEndpoints, StringComparison.Ordinal);
        Assert.Contains("merchant-registration", platformEndpoints, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryModuleManualReferencesExistingScreenshots()
    {
        var manualDirectory = Path.Combine(RepositoryRoot, "docs", "user-manual");
        foreach (var manualPath in Directory.GetFiles(manualDirectory, "*.md"))
        {
            var imagePaths = MarkdownImagePattern().Matches(File.ReadAllText(manualPath))
                .Select(match => match.Groups[1].Value).ToList();
            Assert.All(imagePaths, relativePath =>
                Assert.True(File.Exists(Path.Combine(manualDirectory, relativePath)),
                    $"用户手册截图不存在：{Path.GetFileName(manualPath)} -> {relativePath}"));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ERP.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("无法定位 ERP 仓库根目录");
    }

    [GeneratedRegex("^V([0-9]+)__[a-z0-9_]+\\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex MigrationNamePattern();

    [GeneratedRegex("!\\[[^\\]]*\\]\\((assets/[^)]+)\\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownImagePattern();
}
