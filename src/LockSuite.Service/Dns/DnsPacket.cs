using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace LockSuite.Service.Dns;

public sealed class DnsRecord
{
    public string Name { get; set; } = "";
    public ushort Type { get; set; }
    public ushort Class { get; set; }
    public uint Ttl { get; set; }
    public IPAddress? IpAddress { get; set; }
}

public sealed class DnsPacket
{
    public ushort TransactionId { get; set; }
    public ushort Flags { get; set; }
    public string QuestionName { get; set; } = "";
    public ushort QuestionType { get; set; }
    public ushort QuestionClass { get; set; }
    public List<DnsRecord> Answers { get; } = new();

    public bool IsQuery => (Flags & 0x8000) == 0;
    public bool RecursionDesired => (Flags & 0x0100) != 0;

    public const ushort TypeA = 1;
    public const ushort TypeAAAA = 28;
    public const ushort TypeCNAME = 5;

    public static DnsPacket? Parse(byte[] buffer, int length)
    {
        if (buffer == null || length < 12) return null;

        try
        {
            var packet = new DnsPacket();
            packet.TransactionId = (ushort)((buffer[0] << 8) | buffer[1]);
            packet.Flags = (ushort)((buffer[2] << 8) | buffer[3]);
            ushort qdCount = (ushort)((buffer[4] << 8) | buffer[5]);
            ushort anCount = (ushort)((buffer[6] << 8) | buffer[7]);

            int offset = 12;
            if (qdCount > 0 && offset < length)
            {
                packet.QuestionName = ReadDomainName(buffer, ref offset, length);
                if (offset + 4 <= length)
                {
                    packet.QuestionType = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                    packet.QuestionClass = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);
                    offset += 4;
                }
            }

            for (int i = 0; i < anCount && offset < length; i++)
            {
                string answerName = ReadDomainName(buffer, ref offset, length);
                if (offset + 10 > length) break;

                ushort type = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
                ushort cls = (ushort)((buffer[offset + 2] << 8) | buffer[offset + 3]);
                uint ttl = ((uint)buffer[offset + 4] << 24) |
                           ((uint)buffer[offset + 5] << 16) |
                           ((uint)buffer[offset + 6] << 8) |
                           buffer[offset + 7];
                ushort rdLength = (ushort)((buffer[offset + 8] << 8) | buffer[offset + 9]);
                offset += 10;

                IPAddress? ip = null;
                if (type == TypeA && rdLength == 4 && offset + 4 <= length)
                {
                    byte[] ipBytes = new byte[4];
                    Buffer.BlockCopy(buffer, offset, ipBytes, 0, 4);
                    ip = new IPAddress(ipBytes);
                }
                else if (type == TypeAAAA && rdLength == 16 && offset + 16 <= length)
                {
                    byte[] ipBytes = new byte[16];
                    Buffer.BlockCopy(buffer, offset, ipBytes, 0, 16);
                    ip = new IPAddress(ipBytes);
                }

                offset += rdLength;

                packet.Answers.Add(new DnsRecord
                {
                    Name = answerName,
                    Type = type,
                    Class = cls,
                    Ttl = ttl,
                    IpAddress = ip
                });
            }

            return packet;
        }
        catch
        {
            return null;
        }
    }

    public static byte[] CreateNxDomainResponse(byte[] queryBuffer, int queryLength)
    {
        if (queryBuffer == null || queryLength < 12)
        {
            return Array.Empty<byte>();
        }

        // Buscamos el final de la seccion de pregunta para copiarla exactamente
        int offset = 12;
        while (offset < queryLength)
        {
            byte len = queryBuffer[offset];
            if (len == 0)
            {
                offset += 5; // byte nulo + QType (2B) + QClass (2B)
                break;
            }
            if ((len & 0xC0) == 0xC0)
            {
                offset += 6; // Puntero de compresión (2B) + QType (2B) + QClass (2B)
                break;
            }
            offset += 1 + len;
        }

        int questionEnd = Math.Min(offset, queryLength);
        byte[] response = new byte[questionEnd];
        Buffer.BlockCopy(queryBuffer, 0, response, 0, questionEnd);

        // Transaction ID queda intacto (bytes 0 y 1)

        // Flags en bytes 2 y 3:
        // QR = 1 (Respuesta: 0x80)
        // RD = RD original (bit 8 de queryBuffer[2])
        // RA = 1 (Recursión disponible: 0x80 en byte 3)
        // RCODE = 3 (NXDOMAIN: 0x03 en los 4 bits bajos del byte 3)
        byte origRd = (byte)(queryBuffer[2] & 0x01);
        response[2] = (byte)(0x80 | (origRd != 0 ? 0x01 : 0x00)); // QR=1, RD=preservado
        response[3] = 0x83;                                        // RA=1, RCODE=3 (NXDOMAIN)

        // Contadores:
        // QDCount = 1 (preservado de queryBuffer)
        // ANCount = 0
        response[6] = 0; response[7] = 0;
        // NSCount = 0
        response[8] = 0; response[9] = 0;
        // ARCount = 0
        response[10] = 0; response[11] = 0;

        return response;
    }

    private static string ReadDomainName(byte[] buffer, ref int offset, int length)
    {
        var sb = new StringBuilder();
        int currentOffset = offset;
        bool jumped = false;
        int maxJumps = 5;

        while (currentOffset < length)
        {
            byte len = buffer[currentOffset];
            if (len == 0)
            {
                currentOffset++;
                if (!jumped) offset = currentOffset;
                break;
            }

            if ((len & 0xC0) == 0xC0)
            {
                // Puntero de compresión (14 bits)
                if (currentOffset + 1 >= length) break;
                int pointer = ((len & 0x3F) << 8) | buffer[currentOffset + 1];
                if (!jumped) offset = currentOffset + 2;
                jumped = true;
                currentOffset = pointer;
                if (--maxJumps < 0) break; // Prevenir loops
                continue;
            }

            currentOffset++;
            if (currentOffset + len > length) break;

            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(buffer, currentOffset, len));
            currentOffset += len;

            if (!jumped) offset = currentOffset;
        }

        return sb.ToString();
    }
}
