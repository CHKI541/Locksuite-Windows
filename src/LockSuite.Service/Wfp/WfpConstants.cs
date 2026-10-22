using System;

namespace LockSuite.Service.Wfp;

public static class WfpConstants
{
    // Subcapa propia de LockSuite
    public static readonly Guid LockSuiteSubLayerGuid = new("8E8E939C-8A95-46D9-8976-96F6EB6CE522");
    public const ushort LockSuiteSubLayerWeight = 0xEE00; // Peso alto

    // Capas WFP (ALE - Application Layer Enforcement)
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V4 = new("c38d57d1-05a7-4c33-904f-7fbab0170322");
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V6 = new("4a72393b-319f-44bc-84c2-8706ec73689e");

    // Campos de condición
    public static readonly Guid FWPM_CONDITION_IP_REMOTE_ADDRESS = new("17e50882-b131-4148-be58-6a56e9c1c5cb");
    public static readonly Guid FWPM_CONDITION_IP_LOCAL_PORT     = new("0c1ba161-0021-4f93-94c6-e5c9a752250c");
    public static readonly Guid FWPM_CONDITION_IP_REMOTE_PORT    = new("c35a604d-d22b-4e1a-91b4-68f674ee674b");
    public static readonly Guid FWPM_CONDITION_IP_PROTOCOL       = new("0da4b870-652a-4393-8025-a6bfb30dcf28");
    public static readonly Guid FWPM_CONDITION_ALE_APP_ID        = new("d78de288-7608-403b-b4a6-f2d480e64032");
    public static readonly Guid FWPM_CONDITION_FLAGS             = new("9cf76f7b-99d8-4f11-9a74-a0352ef2e5b6");

    // Tipos de datos WFP
    public const uint FWP_EMPTY = 0;
    public const uint FWP_UINT8 = 1;
    public const uint FWP_UINT16 = 2;
    public const uint FWP_UINT32 = 3;
    public const uint FWP_UINT64 = 4;
    public const uint FWP_BYTE_BLOB_TYPE = 14;
    public const uint FWP_V4_ADDR_MASK = 16;
    public const uint FWP_BYTE_ARRAY16_TYPE = 17;

    // Tipos de comparación (FWP_MATCH_TYPE)
    public const uint FWP_MATCH_EQUAL = 0;
    public const uint FWP_MATCH_GREATER = 1;
    public const uint FWP_MATCH_LESS = 2;
    public const uint FWP_MATCH_FLAGS_ALL_SET = 8;

    // Flags de condición
    public const uint FWP_CONDITION_FLAG_IS_LOOPBACK = 0x00000001;

    // Acciones
    public const uint FWP_ACTION_FLAG_TERMINATING = 0x00001000;
    public const uint FWP_ACTION_BLOCK = 0x00000001 | FWP_ACTION_FLAG_TERMINATING;
    public const uint FWP_ACTION_PERMIT = 0x00000002 | FWP_ACTION_FLAG_TERMINATING;

    // Protocolos IP
    public const byte IPPROTO_TCP = 6;
    public const byte IPPROTO_UDP = 17;

    // Puertos conocidos
    public const ushort PORT_NTP = 123;          // sincronizacion de hora
    public const ushort PORT_DNS = 53;
    public const ushort PORT_DHCP_SERVER = 67;
    public const ushort PORT_DHCP_CLIENT = 68;
    public const ushort PORT_DHCPV6_CLIENT = 546;
    public const ushort PORT_DHCPV6_SERVER = 547;
    public const ushort PORT_HTTP = 80;
    public const ushort PORT_HTTPS = 443;
}
