using System;

namespace LockSuite.Core;

/// <summary>
/// Extrae el campo SNI (server_name) de un ClientHello de TLS.
/// El SNI viaja en texto plano, antes del cifrado, asi que permite filtrar
/// por dominio sin descifrar nada (sin CA propia, sin MITM).
/// Es defensivo: ante cualquier largo invalido o paquete truncado devuelve null,
/// nunca lanza excepcion ni lee fuera de rango.
/// </summary>
public static class SniParser
{
    /// <summary>Devuelve el host del SNI, o null si no hay o el paquete es invalido.</summary>
    public static string? TryParse(ReadOnlySpan<byte> data)
    {
        try
        {
            // Record TLS: content_type(1) version(2) length(2)
            if (data.Length < 5) return null;
            if (data[0] != 0x16) return null;                 // 22 = handshake
            int recLen = (data[3] << 8) | data[4];
            int bodyEnd = Math.Min(5 + recLen, data.Length);
            var body = data[5..bodyEnd];

            // Handshake: type(1) length(3)
            if (body.Length < 4) return null;
            if (body[0] != 0x01) return null;                 // 1 = ClientHello
            int hsLen = (body[1] << 16) | (body[2] << 8) | body[3];
            int hsEnd = Math.Min(4 + hsLen, body.Length);
            var hs = body[4..hsEnd];

            int p = 0;
            p += 2 + 32;                                      // client_version + random
            if (p + 1 > hs.Length) return null;
            int sidLen = hs[p]; p += 1 + sidLen;              // session_id
            if (p + 2 > hs.Length) return null;
            int csLen = (hs[p] << 8) | hs[p + 1]; p += 2 + csLen;   // cipher_suites
            if (p + 1 > hs.Length) return null;
            int cmLen = hs[p]; p += 1 + cmLen;                // compression_methods
            if (p + 2 > hs.Length) return null;
            int extTotal = (hs[p] << 8) | hs[p + 1]; p += 2;  // extensions
            int end = Math.Min(p + extTotal, hs.Length);

            while (p + 4 <= end)
            {
                int etype = (hs[p] << 8) | hs[p + 1];
                int elen = (hs[p + 2] << 8) | hs[p + 3];
                p += 4;
                if (etype == 0x0000)                          // server_name
                {
                    int sp = p;
                    if (sp + 2 > hs.Length) return null;
                    int listLen = (hs[sp] << 8) | hs[sp + 1]; sp += 2;
                    int listEnd = Math.Min(sp + listLen, hs.Length);
                    while (sp + 3 <= listEnd)
                    {
                        int ntype = hs[sp]; sp += 1;
                        int nlen = (hs[sp] << 8) | hs[sp + 1]; sp += 2;
                        if (ntype == 0x00)                    // host_name
                        {
                            if (sp + nlen > hs.Length || nlen == 0) return null;
                            return System.Text.Encoding.ASCII.GetString(hs.Slice(sp, nlen));
                        }
                        sp += nlen;
                    }
                }
                p += elen;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }
}
