using System.Net;
using System.Net.Http.Headers;
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

    private readonly LegacyEndpointPolicy _policy;
    private readonly HttpClient _client;

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
            Timeout = TimeSpan.FromSeconds(30)
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
        var uri = entity.BuildPageUri(page, pageSize);
        using var response = await SendAsync(HttpMethod.Get, uri, null, cancellationToken);
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
            throw new LegacyMigrationException("旧系统会话已失效，分页导出已安全停止。");
        }

        return json;
    }

    public async Task<string> GetCustomerEditPageAsync(long sourceCustomerId, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            LegacyEndpointPolicy.Origin,
            $"/swshop/base/member.php?act=adds&wintop=N&winpid=2&id={sourceCustomerId}");
        using var response = await SendAsync(HttpMethod.Get, uri, null, cancellationToken);
        EnsureSuccess(response);
        var payload = await ReadLimitedAsync(response.Content, MaximumCustomerEditBytes, cancellationToken);
        var html = Encoding.UTF8.GetString(payload);
        if (html.Contains("login/login.php", StringComparison.OrdinalIgnoreCase))
        {
            throw new LegacyMigrationException("旧系统会话已失效，照片索引导出已安全停止。");
        }

        return html;
    }

    public async Task<LegacyImagePayload> GetCustomerPhotoAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, uri, null, cancellationToken);
        EnsureSuccess(response);
        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (mediaType is not ("image/jpeg" or "image/png" or "image/webp"))
        {
            throw new LegacyMigrationException("旧系统顾客照片响应不是允许的图片格式。");
        }

        var bytes = await ReadLimitedAsync(response.Content, MaximumCustomerPhotoBytes, cancellationToken);
        return new LegacyImagePayload(mediaType, bytes);
    }

    public void Dispose() => _client.Dispose();

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        _policy.EnsureAllowed(method, uri);
        using var request = new HttpRequestMessage(method, uri)
        {
            Content = content
        };
        request.Headers.Referrer = new Uri(LegacyEndpointPolicy.Origin, "/swshop/login/index.php");

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
