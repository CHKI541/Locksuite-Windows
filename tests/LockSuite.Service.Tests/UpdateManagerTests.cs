using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LockSuite.Service.Updates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class UpdateManagerTests
{
    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Fact]
    public async Task CheckAndApplyUpdate_ReturnsFalse_WhenVersionIsSameOrOlder()
    {
        string json = """
        {
            "tag_name": "v0.2.0",
            "name": "LockSuite Windows v0.2.0",
            "assets": [
                {
                    "name": "LockSuiteSetup-0.2.0.exe",
                    "browser_download_url": "https://example.com/setup.exe"
                }
            ]
        }
        """;

        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

        using var client = new HttpClient(handler);
        using var manager = new UpdateManager(NullLogger<UpdateManager>.Instance, client);

        bool result = await manager.CheckAndApplyUpdateAsync(CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task CheckAndApplyUpdate_ReturnsFalse_WhenApiReturnsError()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(handler);
        using var manager = new UpdateManager(NullLogger<UpdateManager>.Instance, client);

        bool result = await manager.CheckAndApplyUpdateAsync(CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task CheckAndApplyUpdate_ReturnsFalse_WhenJsonIsInvalid()
    {
        var handler = new MockHttpMessageHandler(req => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"invalid\": true}", Encoding.UTF8, "application/json")
        });
        using var client = new HttpClient(handler);
        using var manager = new UpdateManager(NullLogger<UpdateManager>.Instance, client);

        bool result = await manager.CheckAndApplyUpdateAsync(CancellationToken.None);
        Assert.False(result);
    }

    // --- Verificación de integridad (SHA-256) del instalador ---

    [Fact]
    public void TryGetExpectedSha256_FindsHash_ClassicFormat()
    {
        const string h = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        string sums = h + "  LockSuiteSetup-0.3.0.exe\n";
        bool ok = UpdateManager.TryGetExpectedSha256(sums, "LockSuiteSetup-0.3.0.exe", out string hash);
        Assert.True(ok);
        Assert.Equal(h, hash);
    }

    [Fact]
    public void TryGetExpectedSha256_StarFormat_AndCaseInsensitiveName()
    {
        const string upper = "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789";
        string sums = upper + " *locksuitesetup-0.3.0.exe";
        bool ok = UpdateManager.TryGetExpectedSha256(sums, "LockSuiteSetup-0.3.0.exe", out string hash);
        Assert.True(ok);
        Assert.Equal(upper.ToLowerInvariant(), hash);
    }

    [Fact]
    public void TryGetExpectedSha256_ReturnsFalse_WhenAssetMissing()
    {
        const string h = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";
        string sums = h + "  otro.exe";
        bool ok = UpdateManager.TryGetExpectedSha256(sums, "LockSuiteSetup-0.3.0.exe", out string hash);
        Assert.False(ok);
        Assert.Equal("", hash);
    }

    [Fact]
    public void ComputeSha256_MatchesKnownVector()
    {
        // SHA-256 de "abc"
        string tmp = Path.Combine(Path.GetTempPath(), "ls_sha_" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllText(tmp, "abc");
            string hash = UpdateManager.ComputeSha256(tmp);
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
