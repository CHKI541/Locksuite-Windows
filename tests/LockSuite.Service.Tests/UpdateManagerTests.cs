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
}
