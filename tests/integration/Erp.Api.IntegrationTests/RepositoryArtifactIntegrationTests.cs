using System.Text.RegularExpressions;
using Erp.Infrastructure.Customers;

namespace Erp.Api.IntegrationTests;

public sealed partial class RepositoryArtifactIntegrationTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

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
        Assert.Contains("ServiceRecordOperators = [SystemRoles.Owner, SystemRoles.StoreManager]", endpoints,
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
        Assert.Contains("ConfigurationOperators = [SystemRoles.Owner, SystemRoles.StoreManager]", endpoints,
            StringComparison.Ordinal);
        Assert.Contains("RequireRole(SystemRoles.Owner)", endpoints, StringComparison.Ordinal);
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
        Assert.Contains("x.HomeStoreId == command.StoreId", facilityService, StringComparison.Ordinal);
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
        Assert.Contains("RequireRole(SystemRoles.Owner)", endpoints, StringComparison.Ordinal);
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
        Assert.Contains("RequireRole(SystemRoles.Owner)", catalogEndpoints, StringComparison.Ordinal);
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
        Assert.Contains("RequireRole(SystemRoles.Owner)", endpoints, StringComparison.Ordinal);
        Assert.Contains("ResolvePriceRole(command.OperatorRoles)", service, StringComparison.Ordinal);
        Assert.Contains("service_order.price.approval_requested", service, StringComparison.Ordinal);
        Assert.Contains("PRICE_APPROVAL_REQUIRED", order, StringComparison.Ordinal);
        Assert.Contains("PRICE_APPROVAL_SELF_REVIEW_FORBIDDEN", File.ReadAllText(Path.Combine(RepositoryRoot,
            "apps", "api", "Erp.Domain", "Cashier", "PriceOverrideApproval.cs")),
            StringComparison.Ordinal);
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
