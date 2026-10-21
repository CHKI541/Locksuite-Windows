using System;
using System.IO;
using LockSuite.Service.Learning;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LockSuite.Service.Tests;

public class LearningManagerTests : IDisposable
{
    private readonly string _tempLogFile;

    public LearningManagerTests()
    {
        _tempLogFile = Path.Combine(Path.GetTempPath(), "locksuite_log_" + Guid.NewGuid().ToString("N") + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempLogFile))
        {
            try { File.Delete(_tempLogFile); } catch { }
        }
    }

    [Fact]
    public void RecordBlocked_RecordsNewDomainAndIncrementsCount()
    {
        var manager = new LearningManager(NullLogger<LearningManager>.Instance, _tempLogFile);

        manager.RecordBlocked("sitio-bloqueado.com", "192.168.1.50");
        manager.RecordBlocked("sitio-bloqueado.com", "192.168.1.50");

        var recent = manager.GetRecent();
        Assert.Single(recent);
        Assert.Equal("sitio-bloqueado.com", recent[0].Domain);
        Assert.Equal(2, recent[0].Count);
        Assert.Equal("192.168.1.50", recent[0].ClientIp);
    }

    [Fact]
    public void Clear_EmptiesRecentList()
    {
        var manager = new LearningManager(NullLogger<LearningManager>.Instance, _tempLogFile);

        manager.RecordBlocked("uno.com");
        manager.RecordBlocked("dos.com");
        Assert.Equal(2, manager.GetRecent().Count);

        manager.Clear();
        Assert.Empty(manager.GetRecent());
    }

    [Fact]
    public void Flush_PersistsToDiskAndReloads()
    {
        using (var manager = new LearningManager(NullLogger<LearningManager>.Instance, _tempLogFile))
        {
            manager.RecordBlocked("persistido.com");
            manager.Flush();
        }

        using var reload = new LearningManager(NullLogger<LearningManager>.Instance, _tempLogFile);
        Assert.Contains(reload.GetRecent(), e => e.Domain == "persistido.com");
    }
}
