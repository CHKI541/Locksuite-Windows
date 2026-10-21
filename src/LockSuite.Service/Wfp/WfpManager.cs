using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace LockSuite.Service.Wfp;

public sealed class WfpManager : IWfpManager
{
    private readonly ILogger<WfpManager> _logger;
    private readonly object _engineLock = new();
    private IntPtr _engineHandle = IntPtr.Zero;
    private bool _isActive;
    private bool _isSimulationMode;

    // Codigos WFP relevantes
    private const uint FWP_E_ALREADY_EXISTS = 0x80320002;

    // Claves FIJAS de los filtros base. Al ser estables (no aleatorias), la instalacion
    // es idempotente: en cada arranque se borran por clave y se vuelven a crear dentro de
    // una transaccion, por lo que nunca se acumulan duplicados entre reinicios del servicio.
    private static readonly Guid KeyLoopbackV4   = new("9b1d2e01-0001-4a01-8b01-10c6b7e5a001");
    private static readonly Guid KeyLoopbackV6   = new("9b1d2e01-0002-4a01-8b01-10c6b7e5a002");
    private static readonly Guid KeyDhcpV4       = new("9b1d2e01-0003-4a01-8b01-10c6b7e5a003");
    private static readonly Guid KeyLocalDns     = new("9b1d2e01-0004-4a01-8b01-10c6b7e5a004");
    private static readonly Guid KeyServiceAppId = new("9b1d2e01-0005-4a01-8b01-10c6b7e5a005");
    private static readonly Guid KeyBlockV4      = new("9b1d2e01-0006-4a01-8b01-10c6b7e5a006");
    private static readonly Guid KeyBlockV6      = new("9b1d2e01-0007-4a01-8b01-10c6b7e5a007");

    private static readonly Guid[] BaseFilterKeys =
    {
        KeyLoopbackV4, KeyLoopbackV6, KeyDhcpV4, KeyLocalDns, KeyServiceAppId, KeyBlockV4, KeyBlockV6
    };

    // Control de reglas dinamicas (por IP) con claves aleatorias, rastreadas por id en memoria.
    private readonly ConcurrentDictionary<ulong, DynamicRuleInfo> _dynamicRules = new();
    private readonly ConcurrentDictionary<string, ulong> _ipToFilterId = new();
    private Timer? _expirationTimer;
    private Timer? _autoRollbackTimer;

    private sealed class DynamicRuleInfo
    {
        public ulong FilterId { get; init; }
        public IPAddress IpAddress { get; init; } = IPAddress.None;
        public DateTimeOffset ExpiresAt { get; set; }
    }

    public bool IsActive => _isActive;
    public bool IsSimulationMode => _isSimulationMode;
    public int ActiveDynamicRulesCount => _dynamicRules.Count;

    public WfpManager(ILogger<WfpManager> logger)
    {
        _logger = logger;
    }

    public void Initialize(bool failClosed)
    {
        lock (_engineLock)
        {
            if (_isActive) return;

            _logger.LogInformation("Inicializando Windows Filtering Platform (WFP)... FailClosed={FailClosed}", failClosed);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger.LogWarning("Plataforma no es Windows. Activando WFP en modo simulación.");
                _isSimulationMode = true;
                _isActive = true;
                StartExpirationTimer();
                return;
            }

            var session = new FWPM_SESSION0
            {
                sessionKey = Guid.NewGuid(),
                displayData = new FWPM_DISPLAY_DATA0
                {
                    name = "LockSuite Filtering Session",
                    description = "Filtro por lista blanca para LockSuite Windows"
                },
                // failClosed=true  -> sesion NO dinamica (flags=0): si el proceso muere, los
                //                     filtros base persisten (la red queda bloqueada = fail-closed)
                //                     hasta el reinicio; en el arranque se re-aplican idempotentes.
                // failClosed=false -> sesion dinamica (flags=1): si el proceso muere, Windows
                //                     remueve los filtros (fail-open explicito).
                flags = failClosed ? 0u : 1u,
                txnWaitTimeoutInMSec = 5000
            };

            uint status = NativeMethods.FwpmEngineOpen0(null, 0, IntPtr.Zero, in session, out _engineHandle);
            if (status != 0)
            {
                _logger.LogWarning("FwpmEngineOpen0 retornó código 0x{Status:X8}. Se requiere elevación de Administrador. Activando modo simulación.", status);
                _engineHandle = IntPtr.Zero;
                _isSimulationMode = true;
                _isActive = true;
                StartExpirationTimer();
                return;
            }

            try
            {
                InstallBaseFiltersTransacted();
                _isActive = true;
                _isSimulationMode = false;
                StartExpirationTimer();
                _logger.LogInformation("WFP inicializado exitosamente en modo kernel. Subcapa y filtros base aplicados.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al instalar subcapa y filtros base en WFP. Efectuando rollback y pasando a simulación.");
                Rollback();
                _isSimulationMode = true;
                _isActive = true;
                StartExpirationTimer();
            }
        }
    }

    public ulong AllowIpTemporary(IPAddress ip, TimeSpan duration)
    {
        if (ip == null || ip.Equals(IPAddress.None) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6None))
            return 0;

        string ipKey = ip.ToString();
        var expiresAt = DateTimeOffset.UtcNow.Add(duration);

        if (_ipToFilterId.TryGetValue(ipKey, out ulong existingFilterId))
        {
            if (_dynamicRules.TryGetValue(existingFilterId, out var existingInfo))
            {
                if (expiresAt > existingInfo.ExpiresAt)
                {
                    existingInfo.ExpiresAt = expiresAt;
                }
                return existingFilterId;
            }
        }

        ulong filterId = 0;
        if (!_isSimulationMode && _engineHandle != IntPtr.Zero)
        {
            lock (_engineLock)
            {
                if (_engineHandle != IntPtr.Zero)
                {
                    filterId = AddIpFilter(ip);
                }
            }
        }
        else
        {
            // En simulación asignamos un ID secuencial simulado
            filterId = (ulong)Random.Shared.Next(100000, 999999);
        }

        if (filterId != 0)
        {
            var info = new DynamicRuleInfo
            {
                FilterId = filterId,
                IpAddress = ip,
                ExpiresAt = expiresAt
            };
            _dynamicRules[filterId] = info;
            _ipToFilterId[ipKey] = filterId;

            _logger.LogDebug("Regla WFP dinámica agregada: IP={IP}, Duración={Seconds}s, FilterId={Id}",
                ipKey, duration.TotalSeconds, filterId);
        }

        return filterId;
    }

    public void RemoveDynamicRule(ulong filterId)
    {
        if (_dynamicRules.TryRemove(filterId, out var info))
        {
            _ipToFilterId.TryRemove(info.IpAddress.ToString(), out _);

            if (!_isSimulationMode && _engineHandle != IntPtr.Zero)
            {
                lock (_engineLock)
                {
                    if (_engineHandle != IntPtr.Zero)
                    {
                        NativeMethods.FwpmFilterDeleteById0(_engineHandle, filterId);
                    }
                }
            }

            _logger.LogDebug("Regla WFP dinámica removida: IP={IP}, FilterId={Id}", info.IpAddress, filterId);
        }
    }

    public void RemoveAllDynamicRules()
    {
        foreach (var rule in _dynamicRules.Values)
        {
            RemoveDynamicRule(rule.FilterId);
        }
    }

    public void Rollback()
    {
        lock (_engineLock)
        {
            CancelAutoRollback();
            RemoveAllDynamicRules();

            if (_engineHandle != IntPtr.Zero)
            {
                // Borrado por CLAVE (no por id): limpia de forma confiable incluso filtros
                // base que hayan quedado de una instancia previa.
                foreach (var key in BaseFilterKeys)
                {
                    var k = key;
                    NativeMethods.FwpmFilterDeleteByKey0(_engineHandle, in k);
                }

                NativeMethods.FwpmSubLayerDeleteByKey0(_engineHandle, in WfpConstants.LockSuiteSubLayerGuid);
                NativeMethods.FwpmEngineClose0(_engineHandle);
                _engineHandle = IntPtr.Zero;
            }

            _isActive = false;
            _logger.LogInformation("WFP Rollback completado. Todas las reglas y subcapa han sido removidas.");
        }
    }

    /// <summary>
    /// Limpieza total fuera del ciclo de vida normal del servicio (para --reset-wfp y el
    /// desinstalador). Abre su propio motor, borra por clave los filtros base y la subcapa,
    /// y cierra. Seguro de llamar aunque no exista nada (ignora "no encontrado").
    /// </summary>
    public void HardReset()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            _logger.LogInformation("HardReset omitido (plataforma no Windows).");
            return;
        }

        lock (_engineLock)
        {
            var session = new FWPM_SESSION0
            {
                sessionKey = Guid.NewGuid(),
                displayData = new FWPM_DISPLAY_DATA0 { name = "LockSuite Reset Session", description = "Limpieza de filtros LockSuite" },
                flags = 0,
                txnWaitTimeoutInMSec = 5000
            };

            uint status = NativeMethods.FwpmEngineOpen0(null, 0, IntPtr.Zero, in session, out IntPtr handle);
            if (status != 0 || handle == IntPtr.Zero)
            {
                _logger.LogWarning("HardReset: no se pudo abrir el motor WFP (0x{Status:X8}). ¿Falta elevación?", status);
                return;
            }

            try
            {
                foreach (var key in BaseFilterKeys)
                {
                    var k = key;
                    NativeMethods.FwpmFilterDeleteByKey0(handle, in k);
                }
                NativeMethods.FwpmSubLayerDeleteByKey0(handle, in WfpConstants.LockSuiteSubLayerGuid);
                _logger.LogInformation("HardReset: filtros base y subcapa de LockSuite removidos de WFP.");
            }
            finally
            {
                NativeMethods.FwpmEngineClose0(handle);
            }
        }
    }

    public void StartAutoRollback(TimeSpan timeout, Action? onRollback = null)
    {
        CancelAutoRollback();
        _logger.LogWarning("Iniciando auto-rollback de seguridad en {Seconds} segundos...", timeout.TotalSeconds);
        _autoRollbackTimer = new Timer(_ =>
        {
            _logger.LogWarning("Tiempo de auto-rollback cumplido. Revirtiendo filtros WFP por seguridad.");
            Rollback();
            onRollback?.Invoke();
        }, null, timeout, Timeout.InfiniteTimeSpan);
    }

    public void CancelAutoRollback()
    {
        _autoRollbackTimer?.Dispose();
        _autoRollbackTimer = null;
    }

    /// <summary>
    /// Instala la subcapa y los filtros base de forma ATOMICA (transaccion WFP): o se aplican
    /// todos o ninguno, y nunca hay un estado intermedio visible (p.ej. permits sin el block).
    /// Es idempotente: borra por clave los filtros base previos antes de re-crearlos.
    /// </summary>
    private void InstallBaseFiltersTransacted()
    {
        uint tx = NativeMethods.FwpmTransactionBegin0(_engineHandle, 0);
        if (tx != 0)
        {
            throw new InvalidOperationException($"FwpmTransactionBegin0 falló. Código: 0x{tx:X8}");
        }

        try
        {
            EnsureSubLayer();

            // Borra los filtros base previos por clave (idempotencia). "No encontrado" se ignora.
            foreach (var key in BaseFilterKeys)
            {
                var k = key;
                NativeMethods.FwpmFilterDeleteByKey0(_engineHandle, in k);
            }

            // 1. Permit Loopback (IPv4 y IPv6)
            AddLoopbackPermitFilter(KeyLoopbackV4, WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4);
            AddLoopbackPermitFilter(KeyLoopbackV6, WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V6);

            // 2. Permit DHCP (Local 68 / Remote 67 UDP)
            AddDhcpPermitFilter(KeyDhcpV4);

            // 3. Permit DNS al resolutor local (127.0.0.1:53)
            AddLocalDnsPermitFilter(KeyLocalDns);

            // 4. Permit salida para el ejecutable del servicio (para consultar DNS upstream)
            AddServiceAppIdPermitFilter(KeyServiceAppId);

            // 5. Default-Deny Outbound (IPv4 y IPv6) - Regla terminante de menor peso en la subcapa
            AddDefaultBlockFilter(KeyBlockV4, WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4, "Default Block Outbound IPv4");
            AddDefaultBlockFilter(KeyBlockV6, WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V6, "Default Block Outbound IPv6");

            uint commit = NativeMethods.FwpmTransactionCommit0(_engineHandle);
            if (commit != 0)
            {
                throw new InvalidOperationException($"FwpmTransactionCommit0 falló. Código: 0x{commit:X8}");
            }
        }
        catch
        {
            NativeMethods.FwpmTransactionAbort0(_engineHandle);
            throw;
        }
    }

    private void EnsureSubLayer()
    {
        var subLayer = new FWPM_SUBLAYER0
        {
            subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
            displayData = new FWPM_DISPLAY_DATA0
            {
                name = "LockSuite SubLayer",
                description = "Subcapa de alta prioridad para filtrado de salida LockSuite"
            },
            flags = 0,
            defaultSD = IntPtr.Zero,
            weight = WfpConstants.LockSuiteSubLayerWeight
        };

        uint status = NativeMethods.FwpmSubLayerAdd0(_engineHandle, in subLayer, IntPtr.Zero);
        if (status != 0 && status != FWP_E_ALREADY_EXISTS)
        {
            throw new InvalidOperationException($"Error al crear subcapa en WFP. Código: 0x{status:X8}");
        }
    }

    private void AddLoopbackPermitFilter(Guid filterKey, Guid layerGuid)
    {
        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = WfpConstants.FWPM_CONDITION_FLAGS,
            matchType = WfpConstants.FWP_MATCH_FLAGS_ALL_SET,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = WfpConstants.FWP_UINT32,
                uint32 = WfpConstants.FWP_CONDITION_FLAG_IS_LOOPBACK
            }
        };

        IntPtr condPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
        try
        {
            Marshal.StructureToPtr(condition, condPtr, false);

            var filter = new FWPM_FILTER0
            {
                filterKey = filterKey,
                displayData = new FWPM_DISPLAY_DATA0
                {
                    name = "LockSuite Permit Loopback",
                    description = "Permite todo el trafico local de loopback"
                },
                layerKey = layerGuid,
                subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 50 },
                numFilterConditions = 1,
                filterCondition = condPtr,
                action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
            };

            AddFilterChecked(in filter, "Permit Loopback");
        }
        finally
        {
            Marshal.FreeHGlobal(condPtr);
        }
    }

    private void AddDhcpPermitFilter(Guid filterKey)
    {
        var conditions = new FWPM_FILTER_CONDITION0[2];
        conditions[0] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = WfpConstants.FWPM_CONDITION_IP_PROTOCOL,
            matchType = WfpConstants.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = WfpConstants.FWP_UINT8,
                uint8 = WfpConstants.IPPROTO_UDP
            }
        };
        conditions[1] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = WfpConstants.FWPM_CONDITION_IP_REMOTE_PORT,
            matchType = WfpConstants.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = WfpConstants.FWP_UINT16,
                uint16 = WfpConstants.PORT_DHCP_SERVER
            }
        };

        int itemSize = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
        IntPtr condArrayPtr = Marshal.AllocHGlobal(itemSize * conditions.Length);
        try
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                IntPtr itemPtr = IntPtr.Add(condArrayPtr, i * itemSize);
                Marshal.StructureToPtr(conditions[i], itemPtr, false);
            }

            var filter = new FWPM_FILTER0
            {
                filterKey = filterKey,
                displayData = new FWPM_DISPLAY_DATA0
                {
                    name = "LockSuite Permit DHCP Outbound",
                    description = "Permite DHCP para obtencion de direccion de red"
                },
                layerKey = WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 50 },
                numFilterConditions = (uint)conditions.Length,
                filterCondition = condArrayPtr,
                action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
            };

            AddFilterChecked(in filter, "Permit DHCP");
        }
        finally
        {
            Marshal.FreeHGlobal(condArrayPtr);
        }
    }

    private void AddLocalDnsPermitFilter(Guid filterKey)
    {
        var conditions = new FWPM_FILTER_CONDITION0[2];
        conditions[0] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = WfpConstants.FWPM_CONDITION_IP_REMOTE_PORT,
            matchType = WfpConstants.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = WfpConstants.FWP_UINT16,
                uint16 = WfpConstants.PORT_DNS
            }
        };
        // 127.0.0.1 en host byte order: 0x7F000001
        conditions[1] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = WfpConstants.FWPM_CONDITION_IP_REMOTE_ADDRESS,
            matchType = WfpConstants.FWP_MATCH_EQUAL,
            conditionValue = new FWP_CONDITION_VALUE0
            {
                type = WfpConstants.FWP_UINT32,
                uint32 = 0x7F000001
            }
        };

        int itemSize = Marshal.SizeOf<FWPM_FILTER_CONDITION0>();
        IntPtr condArrayPtr = Marshal.AllocHGlobal(itemSize * conditions.Length);
        try
        {
            for (int i = 0; i < conditions.Length; i++)
            {
                IntPtr itemPtr = IntPtr.Add(condArrayPtr, i * itemSize);
                Marshal.StructureToPtr(conditions[i], itemPtr, false);
            }

            var filter = new FWPM_FILTER0
            {
                filterKey = filterKey,
                displayData = new FWPM_DISPLAY_DATA0
                {
                    name = "LockSuite Permit Local DNS",
                    description = "Permite consultas DNS hacia el resolutor local en 127.0.0.1:53"
                },
                layerKey = WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 50 },
                numFilterConditions = (uint)conditions.Length,
                filterCondition = condArrayPtr,
                action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
            };

            AddFilterChecked(in filter, "Permit Local DNS");
        }
        finally
        {
            Marshal.FreeHGlobal(condArrayPtr);
        }
    }

    private void AddServiceAppIdPermitFilter(Guid filterKey)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            _logger.LogWarning("No se pudo determinar la ruta del ejecutable para el permit AppId del servicio.");
            return;
        }

        uint appStatus = NativeMethods.FwpmGetAppIdFromFileName0(exePath, out IntPtr blobPtr);
        if (appStatus != 0 || blobPtr == IntPtr.Zero)
        {
            _logger.LogWarning("FwpmGetAppIdFromFileName0 falló (0x{Status:X8}). El servicio podría no alcanzar el DNS upstream.", appStatus);
            return;
        }

        try
        {
            var condition = new FWPM_FILTER_CONDITION0
            {
                fieldKey = WfpConstants.FWPM_CONDITION_ALE_APP_ID,
                matchType = WfpConstants.FWP_MATCH_EQUAL,
                conditionValue = new FWP_CONDITION_VALUE0
                {
                    type = WfpConstants.FWP_BYTE_BLOB_TYPE,
                    byteBlob = blobPtr
                }
            };

            IntPtr condPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            try
            {
                Marshal.StructureToPtr(condition, condPtr, false);

                var filter = new FWPM_FILTER0
                {
                    filterKey = filterKey,
                    displayData = new FWPM_DISPLAY_DATA0
                    {
                        name = "LockSuite Permit Service AppId",
                        description = "Permite al propio servicio consultar servidores DNS upstream"
                    },
                    layerKey = WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                    subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                    weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 50 },
                    numFilterConditions = 1,
                    filterCondition = condPtr,
                    action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
                };

                AddFilterChecked(in filter, "Permit Service AppId");
            }
            finally
            {
                Marshal.FreeHGlobal(condPtr);
            }
        }
        finally
        {
            NativeMethods.FwpmFreeMemory0(ref blobPtr);
        }
    }

    private void AddDefaultBlockFilter(Guid filterKey, Guid layerGuid, string name)
    {
        var filter = new FWPM_FILTER0
        {
            filterKey = filterKey,
            displayData = new FWPM_DISPLAY_DATA0
            {
                name = name,
                description = "Bloqueo por defecto de salida (LockSuite default-deny)"
            },
            layerKey = layerGuid,
            subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
            weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 1 }, // Peso 1 = menor prioridad en la subcapa
            numFilterConditions = 0,
            filterCondition = IntPtr.Zero,
            action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_BLOCK }
        };

        AddFilterChecked(in filter, name);
    }

    /// <summary>Agrega un filtro y lanza si el resultado no es éxito ni "ya existe".</summary>
    private void AddFilterChecked(in FWPM_FILTER0 filter, string label)
    {
        uint status = NativeMethods.FwpmFilterAdd0(_engineHandle, in filter, IntPtr.Zero, out _);
        if (status != 0 && status != FWP_E_ALREADY_EXISTS)
        {
            throw new InvalidOperationException($"FwpmFilterAdd0 ('{label}') falló. Código: 0x{status:X8}");
        }
    }

    private ulong AddIpFilter(IPAddress ip)
    {
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] bytes = ip.GetAddressBytes();
            uint ipv4HostOrder = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

            var condition = new FWPM_FILTER_CONDITION0
            {
                fieldKey = WfpConstants.FWPM_CONDITION_IP_REMOTE_ADDRESS,
                matchType = WfpConstants.FWP_MATCH_EQUAL,
                conditionValue = new FWP_CONDITION_VALUE0
                {
                    type = WfpConstants.FWP_UINT32,
                    uint32 = ipv4HostOrder
                }
            };

            IntPtr condPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
            try
            {
                Marshal.StructureToPtr(condition, condPtr, false);

                var filter = new FWPM_FILTER0
                {
                    filterKey = Guid.NewGuid(),
                    displayData = new FWPM_DISPLAY_DATA0
                    {
                        name = $"LockSuite Allow IP {ip}",
                        description = "Regla dinámica temporal abierta por resolución DNS permitida"
                    },
                    layerKey = WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                    subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                    weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 100 },
                    numFilterConditions = 1,
                    filterCondition = condPtr,
                    action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
                };

                uint status = NativeMethods.FwpmFilterAdd0(_engineHandle, in filter, IntPtr.Zero, out ulong id);
                return status == 0 ? id : 0;
            }
            finally
            {
                Marshal.FreeHGlobal(condPtr);
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] bytes = ip.GetAddressBytes();
            IntPtr byte16Ptr = Marshal.AllocHGlobal(16);
            try
            {
                Marshal.Copy(bytes, 0, byte16Ptr, 16);

                var condition = new FWPM_FILTER_CONDITION0
                {
                    fieldKey = WfpConstants.FWPM_CONDITION_IP_REMOTE_ADDRESS,
                    matchType = WfpConstants.FWP_MATCH_EQUAL,
                    conditionValue = new FWP_CONDITION_VALUE0
                    {
                        type = WfpConstants.FWP_BYTE_ARRAY16_TYPE,
                        byteArray16 = byte16Ptr
                    }
                };

                IntPtr condPtr = Marshal.AllocHGlobal(Marshal.SizeOf<FWPM_FILTER_CONDITION0>());
                try
                {
                    Marshal.StructureToPtr(condition, condPtr, false);

                    var filter = new FWPM_FILTER0
                    {
                        filterKey = Guid.NewGuid(),
                        displayData = new FWPM_DISPLAY_DATA0
                        {
                            name = $"LockSuite Allow IPv6 {ip}",
                            description = "Regla dinámica temporal abierta por resolución DNS permitida"
                        },
                        layerKey = WfpConstants.FWPM_LAYER_ALE_AUTH_CONNECT_V6,
                        subLayerKey = WfpConstants.LockSuiteSubLayerGuid,
                        weight = new FWP_VALUE0 { type = WfpConstants.FWP_UINT64, uint64 = 100 },
                        numFilterConditions = 1,
                        filterCondition = condPtr,
                        action = new FWPM_ACTION0 { type = WfpConstants.FWP_ACTION_PERMIT }
                    };

                    uint status = NativeMethods.FwpmFilterAdd0(_engineHandle, in filter, IntPtr.Zero, out ulong id);
                    return status == 0 ? id : 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(condPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(byte16Ptr);
            }
        }

        return 0;
    }

    private void StartExpirationTimer()
    {
        _expirationTimer?.Dispose();
        _expirationTimer = new Timer(CheckExpiredRules, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private void CheckExpiredRules(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var rule in _dynamicRules.Values)
        {
            if (now >= rule.ExpiresAt)
            {
                RemoveDynamicRule(rule.FilterId);
            }
        }
    }

    public void Dispose()
    {
        _expirationTimer?.Dispose();
        _autoRollbackTimer?.Dispose();
        Rollback();
    }
}
