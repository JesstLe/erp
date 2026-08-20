using System.Security.Cryptography;

namespace Erp.LegacyMigration;

public static class LegacyMigrationCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        if (args.Length > 0 && string.Equals(args[0], "profile", StringComparison.Ordinal))
        {
            return await LegacyProfileCli.RunAsync(args, output, cancellationToken);
        }

        if (args.Length > 0 && string.Equals(args[0], "extras", StringComparison.Ordinal))
        {
            return await LegacyExtraExportCli.RunAsync(args, output, cancellationToken);
        }

        if (args.Length > 0 && string.Equals(args[0], "import", StringComparison.Ordinal))
        {
            return await LegacyImportCli.RunAsync(args, output, cancellationToken);
        }

        try
        {
            var options = LegacyCliOptions.Parse(args);
            using var credentials = LegacyCredentials.ReadFromEnvironmentOrConsole(output);

            SecureOutputDirectory.Prepare(options.OutputDirectory);
            await output.WriteLineAsync($"导出目录：{options.OutputDirectory}");

            using var payloadStore = new EncryptedPayloadStore(credentials.ExportKey);
            using var session = new LegacySessionClient(new LegacyEndpointPolicy());

            var captchaPath = Path.Combine(options.OutputDirectory, "captcha.png");
            await session.DownloadCaptchaAsync(captchaPath, cancellationToken);

            var captcha = options.Captcha;
            if (string.IsNullOrWhiteSpace(captcha))
            {
                await output.WriteLineAsync($"验证码图片：{captchaPath}");
                await output.WriteAsync("请输入图片中的四位验证码：");
                captcha = Console.ReadLine();
            }

            ValidateCaptcha(captcha);
            await session.LoginAsync(credentials.Account, credentials.Password, captcha!, cancellationToken);
            SecureFile.TryDelete(captchaPath);
            await output.WriteLineAsync("旧系统登录成功，开始只读分页导出。");

            var engine = new LegacyExportEngine(session, payloadStore, output);
            var entities = LegacyEntityCatalog.Resolve(options.Entity);
            foreach (var entity in entities)
            {
                var result = await engine.ExportAsync(options, entity, cancellationToken);
                await output.WriteLineAsync(
                    $"导出完成：模块={result.Entity}，页数={result.PageCount}，记录数={result.RowCount}，清单={result.ManifestPath}");
            }

            await output.WriteLineAsync($"本次只读导出完成，共 {entities.Count} 个模块。");
            return 0;
        }
        catch (OperationCanceledException)
        {
            await output.WriteLineAsync("操作已取消；已完成页面可从检查点恢复。");
            return 130;
        }
        catch (LegacyMigrationException exception)
        {
            await output.WriteLineAsync($"迁移工具停止：{SensitiveText.Redact(exception.Message)}");
            return 2;
        }
        catch (HttpRequestException)
        {
            await output.WriteLineAsync("迁移工具停止：旧系统网络请求失败；没有跳过失败页面。");
            return 3;
        }
        finally
        {
            LegacyCredentials.ClearProcessSecrets();
        }
    }

    internal static void ValidateCaptcha(string? captcha)
    {
        if (captcha is null || captcha.Length != 4 || captcha.Any(character => character is < '0' or > '9'))
        {
            throw new LegacyMigrationException("验证码必须是四位数字。");
        }
    }
}

public sealed record LegacyCliOptions(
    string Entity,
    string OutputDirectory,
    int PageSize,
    int MaxPages,
    int DelayMilliseconds,
    string? Captcha)
{
    public static LegacyCliOptions Parse(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "export", StringComparison.Ordinal))
        {
            throw new LegacyMigrationException(
                "用法：dotnet run --project tools/Erp.LegacyMigration -- export [--entity customers] [--output 绝对路径] [--captcha 1234]");
        }

        var entity = "customers";
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            "erp-legacy-exports",
            DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ", System.Globalization.CultureInfo.InvariantCulture));
        var pageSize = 100;
        var maxPages = 10_000;
        var delayMilliseconds = 200;
        string? captcha = null;

        for (var index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new LegacyMigrationException($"参数 {args[index]} 缺少值。");
            }

            var name = args[index];
            var value = args[index + 1];
            switch (name)
            {
                case "--entity":
                    entity = value;
                    break;
                case "--output":
                    outputDirectory = value;
                    break;
                case "--page-size":
                    pageSize = ParseBoundedInt(name, value, 1, 200);
                    break;
                case "--max-pages":
                    maxPages = ParseBoundedInt(name, value, 1, 10_000);
                    break;
                case "--delay-ms":
                    delayMilliseconds = ParseBoundedInt(name, value, 0, 5_000);
                    break;
                case "--captcha":
                    captcha = value;
                    break;
                default:
                    throw new LegacyMigrationException($"不支持的参数：{name}");
            }
        }

        _ = LegacyEntityCatalog.Resolve(entity);

        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new LegacyMigrationException("导出目录必须使用绝对路径。");
        }

        return new LegacyCliOptions(
            entity,
            Path.GetFullPath(outputDirectory),
            pageSize,
            maxPages,
            delayMilliseconds,
            captcha);
    }

    private static int ParseBoundedInt(string name, string value, int minimum, int maximum)
    {
        if (!int.TryParse(value, out var result) || result < minimum || result > maximum)
        {
            throw new LegacyMigrationException($"参数 {name} 必须在 {minimum} 到 {maximum} 之间。");
        }

        return result;
    }
}

public sealed class LegacyCredentials : IDisposable
{
    private LegacyCredentials(string account, string password, byte[] exportKey)
    {
        Account = account;
        Password = password;
        ExportKey = exportKey;
    }

    public string Account { get; }

    public string Password { get; }

    public byte[] ExportKey { get; }

    public static LegacyCredentials ReadFromEnvironmentOrConsole(TextWriter output)
    {
        var account = Environment.GetEnvironmentVariable("ERP_LEGACY_ACCOUNT");
        if (string.IsNullOrWhiteSpace(account))
        {
            account = ReadSecret(output, "旧系统账号（输入不回显）：");
        }

        var password = Environment.GetEnvironmentVariable("ERP_LEGACY_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            password = ReadSecret(output, "旧系统密码：");
        }

        var encodedKey = Environment.GetEnvironmentVariable("ERP_LEGACY_EXPORT_KEY");
        if (string.IsNullOrWhiteSpace(encodedKey))
        {
            encodedKey = ReadSecret(output, "导出加密密钥（32字节Base64）：");
        }

        if (string.IsNullOrWhiteSpace(account) || account.Length > 100)
        {
            throw new LegacyMigrationException("旧系统账号格式无效。");
        }

        if (string.IsNullOrEmpty(password) || password.Length > 200)
        {
            throw new LegacyMigrationException("旧系统密码格式无效。");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedKey);
        }
        catch (FormatException exception)
        {
            throw new LegacyMigrationException("导出加密密钥必须是有效的 Base64。", exception);
        }

        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new LegacyMigrationException("导出加密密钥解码后必须恰好为 32 字节。");
        }

        return new LegacyCredentials(account.Trim(), password, key);
    }

    public static void ClearProcessSecrets()
    {
        Environment.SetEnvironmentVariable("ERP_LEGACY_ACCOUNT", null);
        Environment.SetEnvironmentVariable("ERP_LEGACY_PASSWORD", null);
        Environment.SetEnvironmentVariable("ERP_LEGACY_EXPORT_KEY", null);
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(ExportKey);
    }

    private static string ReadSecret(TextWriter output, string prompt)
    {
        if (Console.IsInputRedirected)
        {
            throw new LegacyMigrationException($"{prompt.TrimEnd('：')}未通过安全环境变量提供，且当前终端无法隐藏输入。");
        }

        output.Write(prompt);
        var buffer = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return new string(buffer.ToArray());
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Count > 0)
                {
                    buffer.RemoveAt(buffer.Count - 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar) && buffer.Count < 512)
            {
                buffer.Add(key.KeyChar);
            }
        }
    }
}

public sealed class LegacyMigrationException : Exception
{
    public LegacyMigrationException(string message)
        : base(message)
    {
    }

    public LegacyMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
