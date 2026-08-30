using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Erp.LegacyMigration;

public sealed class LegacySessionClient : IDisposable
{
    private const int MaximumHtmlBytes = 2 * 1024 * 1024;
    private const int MaximumCaptchaBytes = 1024 * 1024;
    private const int MaximumGridBytes = 16 * 1024 * 1024;
    private const int MaximumCustomerEditBytes = 4 * 1024 * 1024;
    private const int MaximumCustomerPhotoBytes = 10 * 1024 * 1024;
    private const int PhotoRangeBytes = 256 * 1024;

    private readonly LegacyEndpointPolicy _policy;
    private readonly HttpClient _client;
    private bool _carePageInitialized;
    private readonly HashSet<string> _initializedDirectLedgerPages = new(StringComparer.Ordinal);

    public LegacySessionClient(LegacyEndpointPolicy policy, HttpMessageHandler? handler = null)
    {
        _policy = policy;
        handler ??= new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer(),
            UseCookies = true
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            // The legacy image host can need several minutes for multi-megabyte originals.
            // Per-photo linked cancellation below enforces the same 300-second ceiling.
            Timeout = TimeSpan.FromSeconds(300)
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ErpLegacyMigration", "1.0"));
        _client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9");
    }

    public async Task DownloadCaptchaAsync(string destinationPath, CancellationToken cancellationToken)
    {
        var loginUri = new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/login.php");
        using (var loginResponse = await SendAsync(HttpMethod.Get, loginUri, null, cancellationToken))
        {
            await EnsureSuccessAndDrainAsync(loginResponse, MaximumHtmlBytes, cancellationToken);
        }

        var showCodeUri = new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/login.php?act=showcode");
        string showCodeHtml;
        using (var showCodeResponse = await SendAsync(HttpMethod.Get, showCodeUri, null, cancellationToken))
        {
            EnsureSuccess(showCodeResponse);
            showCodeHtml = Encoding.UTF8.GetString(
                await ReadLimitedAsync(showCodeResponse.Content, MaximumHtmlBytes, cancellationToken));
        }

        var match = Regex.Match(
            showCodeHtml,
            "<img[^>]+src\\s*=\\s*[\\\"'](?<src>[^\\\"']*image\\.php[^\\\"']*)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));
        if (!match.Success)
        {
            throw new LegacyMigrationException("旧系统验证码页面结构发生变化。");
        }

        var imageUri = new Uri(showCodeUri, WebUtility.HtmlDecode(match.Groups["src"].Value));
        using var imageResponse = await SendAsync(HttpMethod.Get, imageUri, null, cancellationToken);
        EnsureSuccess(imageResponse);
        var mediaType = imageResponse.Content.Headers.ContentType?.MediaType;
        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("旧系统验证码响应不是图片。");
        }

        var image = await ReadLimitedAsync(imageResponse.Content, MaximumCaptchaBytes, cancellationToken);
        await SecureFile.WriteBytesAtomicAsync(destinationPath, image, cancellationToken);
    }

    public async Task LoginAsync(
        string account,
        string password,
        string captcha,
        CancellationToken cancellationToken)
    {
        var loginUri = new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/login.php?act=login");
        using var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("account_user", account),
            new KeyValuePair<string, string>("account_pwd", password),
            new KeyValuePair<string, string>("check_code", captcha)
        ]);
        using var response = await SendAsync(HttpMethod.Post, loginUri, content, cancellationToken);
        EnsureSuccess(response);
        var payload = await ReadLimitedAsync(response.Content, MaximumHtmlBytes, cancellationToken);

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.TryGetProperty("result", out var result) &&
                string.Equals(result.GetString(), "success", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var dialog = root.TryGetProperty("dialog", out var dialogElement)
                ? dialogElement.GetString()
                : null;
            throw new LegacyMigrationException(
                string.IsNullOrWhiteSpace(dialog) ? "旧系统登录失败。" : $"旧系统登录失败：{SensitiveText.Redact(dialog)}");
        }
        catch (JsonException exception)
        {
            throw new LegacyMigrationException("旧系统登录响应格式发生变化。", exception);
        }
    }

    public async Task<string> GetGridPageAsync(
        LegacyEntityDefinition entity,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (entity == LegacyEntityDefinition.CareRecords && !_carePageInitialized)
        {
            var pageUri = new Uri(LegacyEndpointPolicy.Origin, "/swshop/vip/nurse.php");
            using var pageResponse = await SendAsync(HttpMethod.Get, pageUri, null, cancellationToken);
            EnsureSuccess(pageResponse);
            var pagePayload = await ReadLimitedAsync(pageResponse.Content, MaximumHtmlBytes, cancellationToken);
            var pageHtml = Encoding.UTF8.GetString(pagePayload);
            if (LooksLikeLoginPage(pageHtml))
            {
                throw new LegacyMigrationException("旧系统会话已失效，护理列表导出已安全停止。");
            }

            var customUri = new Uri(LegacyEndpointPolicy.Origin, "/swshop/vip/nurse.php?act=custom");
            using var customResponse = await SendAsync(
                HttpMethod.Post,
                customUri,
                new FormUrlEncodedContent([]),
                cancellationToken,
                pageUri,
                isAjax: true);
            await EnsureSuccessAndDrainAsync(customResponse, MaximumHtmlBytes, cancellationToken);

            _carePageInitialized = true;
        }

        if (entity != LegacyEntityDefinition.CareRecords &&
            entity.IncludeFullHistoryFilters &&
            !entity.IsReport &&
            _initializedDirectLedgerPages.Add(entity.Name))
        {
            var pageUri = new Uri(LegacyEndpointPolicy.Origin, entity.Path);
            using var pageResponse = await SendAsync(HttpMethod.Get, pageUri, null, cancellationToken);
            EnsureSuccess(pageResponse);
            var pagePayload = await ReadLimitedAsync(pageResponse.Content, MaximumHtmlBytes, cancellationToken);
            var pageHtml = Encoding.UTF8.GetString(pagePayload);
            if (LooksLikeLoginPage(pageHtml))
            {
                throw new LegacyMigrationException("旧系统会话已失效，分页导出已安全停止。");
            }
        }

        var uri = entity.BuildPageUri(page, pageSize);
        var referrer = entity == LegacyEntityDefinition.CareRecords
            ? new Uri(LegacyEndpointPolicy.Origin, "/swshop/vip/nurse.php")
            : entity.IsReport || entity.IncludeFullHistoryFilters
                ? new Uri(LegacyEndpointPolicy.Origin, entity.Path)
                : null;
        using var response = await SendAsync(
            HttpMethod.Get,
            uri,
            null,
            cancellationToken,
            referrer,
            isAjax: true);
        EnsureSuccess(response);
        var payload = await ReadLimitedAsync(response.Content, MaximumGridBytes, cancellationToken);
        var json = Encoding.UTF8.GetString(payload);

        if (entity == LegacyEntityDefinition.CareRecords && string.IsNullOrWhiteSpace(json))
        {
            return $"{{\"page\":{page},\"total\":0,\"records\":0,\"rows\":[]}}";
        }

        if (json.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal) ||
            json.Contains("login/login.php", StringComparison.OrdinalIgnoreCase))
        {
            var hints = ExtractReadOnlyGridHints(json);
            throw new LegacyMigrationException(hints.Length == 0
                ? "旧系统未返回表格 JSON，分页导出已安全停止。"
                : $"旧系统未返回表格 JSON，页面只读接口提示：{string.Join(", ", hints)}。");
        }

        return json;
    }

    public async Task<string> GetCustomerEditPageAsync(long sourceCustomerId, CancellationToken cancellationToken)
    {
        using var timeout = CreatePhotoReadTimeout(cancellationToken);
        var uri = new Uri(
            LegacyEndpointPolicy.Origin,
            $"/swshop/base/member.php?act=adds&wintop=N&winpid=2&id={sourceCustomerId}");
        using var response = await SendPhotoReadAsync(HttpMethod.Get, uri, timeout, cancellationToken);
        EnsureSuccess(response);
        var payload = await ReadPhotoBodyAsync(
            response.Content,
            MaximumCustomerEditBytes,
            timeout,
            cancellationToken);
        var html = Encoding.UTF8.GetString(payload);
        if (html.Contains("login/login.php", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("旧系统会话已失效，照片索引导出已安全停止。");
        }

        return html;
    }

    private static bool LooksLikeLoginPage(string html) =>
        html.Contains("account_user", StringComparison.OrdinalIgnoreCase) &&
        html.Contains("account_pwd", StringComparison.OrdinalIgnoreCase) &&
        html.Contains("check_code", StringComparison.OrdinalIgnoreCase);

    private static string[] ExtractReadOnlyGridHints(string html) =>
        Regex.Matches(
                html,
                "[A-Za-z0-9_./-]+\\.php\\?act=[A-Za-z0-9_-]*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1))
            .Select(match => match.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

    public async Task<string> GetCareRecordEditPageAsync(long sourceCareRecordId, CancellationToken cancellationToken)
    {
        using var timeout = CreatePhotoReadTimeout(cancellationToken);
        var uri = new Uri(
            LegacyEndpointPolicy.Origin,
            $"/swshop/vip/nurse.php?act=adds&wintop=N&winpid=1&id={sourceCareRecordId}");
        using var response = await SendPhotoReadAsync(
            HttpMethod.Get,
            uri,
            timeout,
            cancellationToken,
            new Uri(LegacyEndpointPolicy.Origin, "/swshop/vip/nurse.php"));
        EnsureSuccess(response);
        var payload = await ReadPhotoBodyAsync(
            response.Content,
            MaximumCustomerEditBytes,
            timeout,
            cancellationToken);
        var html = Encoding.UTF8.GetString(payload);
        if (html.Contains("login/login.php", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("旧系统会话已失效，护理照片索引导出已安全停止。");
        }

        return html;
    }

    public async Task<LegacyImagePayload> GetCustomerPhotoAsync(Uri uri, CancellationToken cancellationToken)
    {
        byte[]? complete = null;
        try
        {
            var offset = 0;
            string? mediaType = null;
            while (true)
            {
                var requestedEnd = offset + PhotoRangeBytes - 1;
                var chunk = await ReadPhotoRangeWithRetryAsync(
                    uri,
                    offset,
                    requestedEnd,
                    cancellationToken);
                if (mediaType is null)
                {
                    mediaType = chunk.MediaType;
                }
                else if (!string.Equals(mediaType, chunk.MediaType, StringComparison.Ordinal))
                {
                    throw new LegacyMigrationException("旧系统照片分段格式不一致，已安全停止。");
                }

                if (!chunk.IsPartial)
                    return new LegacyImagePayload(mediaType, chunk.Bytes);

                var totalLength = chunk.TotalLength.GetValueOrDefault();
                if (totalLength is <= 0 or > MaximumCustomerPhotoBytes ||
                    chunk.Start != offset || chunk.End < chunk.Start ||
                    chunk.Bytes.LongLength != chunk.End - chunk.Start + 1)
                {
                    CryptographicOperations.ZeroMemory(chunk.Bytes);
                    throw new LegacyMigrationException("旧系统照片分段范围无效，已安全停止。");
                }

                complete ??= new byte[(int)totalLength];
                if (complete.LongLength != totalLength)
                {
                    CryptographicOperations.ZeroMemory(chunk.Bytes);
                    throw new LegacyMigrationException("旧系统照片分段总长度发生变化，已安全停止。");
                }
                Buffer.BlockCopy(chunk.Bytes, 0, complete, offset, chunk.Bytes.Length);
                offset += chunk.Bytes.Length;
                CryptographicOperations.ZeroMemory(chunk.Bytes);
                if (offset == complete.Length)
                {
                    var result = complete;
                    complete = null;
                    return new LegacyImagePayload(mediaType, result);
                }
            }
        }
        finally
        {
            if (complete is not null) CryptographicOperations.ZeroMemory(complete);
        }
    }

    private async Task<LegacyPhotoRangePayload> ReadPhotoRangeWithRetryAsync(
        Uri uri,
        int start,
        int end,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var timeout = CreatePhotoReadTimeout(cancellationToken);
                using var response = await SendPhotoRangeAsync(uri, start, end, timeout, cancellationToken);
                EnsureSuccess(response);
                var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
                if (mediaType is not ("image/jpeg" or "image/png" or "image/webp"))
                    throw new LegacyMigrationException("旧系统顾客照片响应不是允许的图片格式。");
                var bytes = await ReadPhotoBodyAsync(
                    response.Content,
                    MaximumCustomerPhotoBytes,
                    timeout,
                    cancellationToken);
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    return new LegacyPhotoRangePayload(mediaType, bytes, false, 0, bytes.Length - 1, bytes.Length);

                var range = response.Content.Headers.ContentRange;
                if (range?.From is null || range.To is null || range.Length is null)
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    throw new LegacyMigrationException("旧系统照片分段响应缺少范围信息。");
                }
                return new LegacyPhotoRangePayload(mediaType, bytes, true, range.From.Value, range.To.Value,
                    range.Length.Value);
            }
            catch (Exception exception) when (
                IsTransientPhotoRead(exception, cancellationToken) && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
            catch (Exception exception) when (IsTransientPhotoRead(exception, cancellationToken))
            {
                throw new LegacyMigrationException("旧系统照片读取暂时失败，已安全停止并保留检查点。", exception);
            }
        }

        throw new LegacyMigrationException("旧系统照片读取暂时失败，已安全停止并保留检查点。");
    }

    private async Task<HttpResponseMessage> SendPhotoRangeAsync(
        Uri uri,
        int start,
        int end,
        CancellationTokenSource timeout,
        CancellationToken callerToken)
    {
        _policy.EnsureAllowed(HttpMethod.Get, uri);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(start, end);
        request.Headers.Referrer = new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/index.php");
        try
        {
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                response.Dispose();
                throw new LegacyMigrationException("旧系统返回了重定向，迁移工具拒绝跟随。");
            }
            return response;
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new LegacyMigrationException("旧系统照片读取超时（300秒），已安全停止并保留检查点。");
        }
    }

    private static bool IsTransientPhotoRead(Exception exception, CancellationToken callerToken) =>
        !callerToken.IsCancellationRequested &&
        (exception is IOException or HttpRequestException ||
         exception is LegacyMigrationException migration &&
         migration.Message.Contains("照片读取超时", StringComparison.Ordinal));

    private static CancellationTokenSource CreatePhotoReadTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(300));
        return timeout;
    }

    private async Task<HttpResponseMessage> SendPhotoReadAsync(
        HttpMethod method,
        Uri uri,
        CancellationTokenSource timeout,
        CancellationToken callerToken,
        Uri? referrer = null)
    {
        try
        {
            return await SendAsync(method, uri, null, timeout.Token, referrer);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new LegacyMigrationException("旧系统照片读取超时（300秒），已安全停止并保留检查点。");
        }
    }

    private static async Task<byte[]> ReadPhotoBodyAsync(
        HttpContent content,
        int maximumBytes,
        CancellationTokenSource timeout,
        CancellationToken callerToken)
    {
        try
        {
            return await ReadLimitedAsync(content, maximumBytes, timeout.Token);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new LegacyMigrationException("旧系统照片读取超时（300秒），已安全停止并保留检查点。");
        }
    }

    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken,
        Uri? referrer = null,
        bool isAjax = false)
    {
        _policy.EnsureAllowed(method, uri);
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };
        request.Headers.Referrer = referrer ?? new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/index.php");
        if (isAjax)
        {
            request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
            request.Headers.Accept.ParseAdd("application/json, text/javascript, */*; q=0.01");
        }

        var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if ((int)response.StatusCode is >= 300 and < 400)
        {
            response.Dispose();
            throw new LegacyMigrationException("旧系统返回了重定向，迁移工具拒绝跟随。");
        }

        return response;
    }

    private static async Task EnsureSuccessAndDrainAsync(
        HttpResponseMessage response,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        EnsureSuccess(response);
        _ = await ReadLimitedAsync(response.Content, maximumBytes, cancellationToken);
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new LegacyMigrationException($"旧系统请求失败，HTTP状态码 {(int)response.StatusCode}。");
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new LegacyMigrationException("旧系统响应超过安全大小限制。");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[81_920];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (output.Length + read > maximumBytes)
            {
                throw new LegacyMigrationException("旧系统响应超过安全大小限制。");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }
}

public sealed record LegacyImagePayload(string ContentType, byte[] Bytes);
internal sealed record LegacyPhotoRangePayload(
    string MediaType,
    byte[] Bytes,
    bool IsPartial,
    long Start,
    long End,
    long? TotalLength);
