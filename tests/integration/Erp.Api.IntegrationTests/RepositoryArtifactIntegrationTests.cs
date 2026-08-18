using System.Text.RegularExpressions;

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
