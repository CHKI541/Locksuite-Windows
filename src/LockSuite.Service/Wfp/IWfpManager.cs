using System;
using System.Net;

namespace LockSuite.Service.Wfp;

public interface IWfpManager : IDisposable
{
    bool IsActive { get; }
    bool IsSimulationMode { get; }
    int ActiveDynamicRulesCount { get; }

    void Initialize(bool failClosed);
    ulong AllowIpTemporary(IPAddress ip, TimeSpan duration);
    void RemoveDynamicRule(ulong filterId);
    void RemoveAllDynamicRules();
    void Rollback();
    void StartAutoRollback(TimeSpan timeout, Action? onRollback = null);
    void CancelAutoRollback();
}
