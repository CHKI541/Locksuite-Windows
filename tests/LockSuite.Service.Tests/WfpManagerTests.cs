using System;
using System.Net;
using System.Threading;
using LockSuite.Service.Wfp;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class WfpManagerTests : IDisposable
{
    private readonly WfpManager _manager;

    public WfpManagerTests()
    {
        _manager = new WfpManager(NullLogger<WfpManager>.Instance);
        _manager.Initialize(failClosed: false);
    }

    public void Dispose()
    {
        _manager.Dispose();
    }

    [Fact]
    public void Initialize_ActivatesEngineOrSimulation()
    {
        Assert.True(_manager.IsActive);
    }

    [Fact]
    public void AllowIpTemporary_RegistersDynamicRule()
    {
        var ip = IPAddress.Parse("93.184.216.34");
        ulong filterId = _manager.AllowIpTemporary(ip, TimeSpan.FromSeconds(60));

        Assert.NotEqual(0ul, filterId);
        Assert.Equal(1, _manager.ActiveDynamicRulesCount);

        // Llamar de nuevo con la misma IP actualiza el TTL sin duplicar la regla
        ulong sameFilterId = _manager.AllowIpTemporary(ip, TimeSpan.FromSeconds(120));
        Assert.Equal(filterId, sameFilterId);
        Assert.Equal(1, _manager.ActiveDynamicRulesCount);
    }

    [Fact]
    public void RemoveDynamicRule_RemovesRuleSuccessfully()
    {
        var ip = IPAddress.Parse("1.1.1.1");
        ulong filterId = _manager.AllowIpTemporary(ip, TimeSpan.FromSeconds(60));
        Assert.Equal(1, _manager.ActiveDynamicRulesCount);

        _manager.RemoveDynamicRule(filterId);
        Assert.Equal(0, _manager.ActiveDynamicRulesCount);
    }

    [Fact]
    public void Rollback_CleansAllRules()
    {
        _manager.AllowIpTemporary(IPAddress.Parse("8.8.8.8"), TimeSpan.FromSeconds(60));
        _manager.AllowIpTemporary(IPAddress.Parse("8.8.4.4"), TimeSpan.FromSeconds(60));
        Assert.Equal(2, _manager.ActiveDynamicRulesCount);

        _manager.Rollback();
        Assert.Equal(0, _manager.ActiveDynamicRulesCount);
        Assert.False(_manager.IsActive);
    }
}
