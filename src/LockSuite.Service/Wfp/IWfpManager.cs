using System;
using System.Net;

namespace LockSuite.Service.Wfp;

public interface IWfpManager : IDisposable
{
    bool IsActive { get; }
    bool IsSimulationMode { get; }
    int ActiveDynamicRulesCount { get; }
    bool AllowLocalNetwork { get; }

    void Initialize(bool failClosed);
    void Initialize(bool failClosed, bool allowLocalNetwork);
    void SetAllowLocalNetwork(bool enabled);
    ulong AllowIpTemporary(IPAddress ip, TimeSpan duration);
    void RemoveDynamicRule(ulong filterId);
    void RemoveAllDynamicRules();
    void Rollback();
    void StartAutoRollback(TimeSpan timeout, Action? onRollback = null);
    void CancelAutoRollback();
}
