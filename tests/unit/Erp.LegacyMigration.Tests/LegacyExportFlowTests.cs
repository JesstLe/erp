using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Erp.LegacyMigration;

namespace Erp.LegacyMigration.Tests;

public sealed class LegacyExportFlowTests
{
    [Fact]
    public async Task LogsInAndExportsPaginatedEncryptedCheckpointedData()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"erp-legacy-flow-{Guid.NewGuid():N}");
        var key = RandomNumberGenerator.GetBytes(32);
        var handler = new FakeLegacyHandler();

        try
        {
            SecureOutputDirectory.Prepare(directory);
            using var session = new LegacySessionClient(new LegacyEndpointPolicy(), handler);
            using var store = new EncryptedPayloadStore(key);

            var captchaPath = Path.Combine(directory, "captcha.png");
            await session.DownloadCaptchaAsync(captchaPath, CancellationToken.None);
            await session.LoginAsync("account", "password", "1234", CancellationToken.None);

            var options = new LegacyCliOptions(
                "customers",
                directory,
                PageSize: 2,
                MaxPages: 10,
                DelayMilliseconds: 0,
                Captcha: "1234");
            var output = new StringWriter();
            var engine = new LegacyExportEngine(session, store, output);

            var result = await engine.ExportAsync(
                options,
                LegacyEntityDefinition.Customers,
                CancellationToken.None);

            Assert.Equal(2, result.PageCount);
            Assert.Equal(3, result.RowCount);
            Assert.True(File.Exists(result.ManifestPath));
            Assert.True(File.Exists(Path.Combine(directory, "customers", "rows.jsonl.enc")));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(Path.Combine(directory, "customers")));
                Assert.Equal(
                    UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(Path.Combine(directory, "customers", "rows.jsonl.enc")));
            }
            Assert.Equal(2, handler.GridRequestCount);
            Assert.All(handler.Requests.Where(request => request.Path.Contains("member.php", StringComparison.Ordinal)),
                request => Assert.Equal(HttpMethod.Get, request.Method));

            _ = await engine.ExportAsync(
                options,
                LegacyEntityDefinition.Customers,
                CancellationToken.None);
            Assert.Equal(2, handler.GridRequestCount);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class FakeLegacyHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        public int GridRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = Assert.IsType<Uri>(request.RequestUri);
            Requests.Add((request.Method, uri.PathAndQuery));

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("login.php", StringComparison.Ordinal) &&
                !uri.Query.Contains("showcode", StringComparison.Ordinal))
            {
                return Task.FromResult(Text("<html>login</html>", "text/html"));
            }

            if (request.Method == HttpMethod.Get && uri.Query.Contains("showcode", StringComparison.Ordinal))
            {
                return Task.FromResult(Text("<img src=\"../public/code/image.php\">", "text/html"));
            }

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("image.php", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([0x89, 0x50, 0x4e, 0x47])
                };
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                return Task.FromResult(response);
            }

            if (request.Method == HttpMethod.Post && uri.Query.Contains("act=login", StringComparison.Ordinal))
            {
                return Task.FromResult(Text("{\"result\":\"success\"}", "application/json"));
            }

            if (request.Method == HttpMethod.Get && uri.AbsolutePath.EndsWith("member.php", StringComparison.Ordinal))
            {
                GridRequestCount++;
                return Task.FromResult(GridRequestCount switch
                {
                    1 => Text(
                        "{\"page\":1,\"total\":2,\"records\":3,\"rows\":[{\"id\":\"1\"},{\"id\":\"2\"}]}",
                        "application/json"),
                    2 => Text(
                        "{\"page\":2,\"total\":2,\"records\":3,\"rows\":[{\"id\":\"3\"}]}",
                        "application/json"),
                    _ => throw new InvalidOperationException("Unexpected extra grid request.")
                });
            }

            throw new InvalidOperationException($"Unexpected request: {request.Method} {uri.PathAndQuery}");
        }

        private static HttpResponseMessage Text(string value, string mediaType) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(value, Encoding.UTF8, mediaType)
            };
    }
}
