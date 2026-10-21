using System;
using System.Net;
using System.Text;
using LockSuite.Service.Dns;
using Xunit;

namespace LockSuite.Service.Tests;

public class DnsPacketTests
{
    [Fact]
    public void Parse_ValidQueryPacket_ExtractsQuestionNameAndType()
    {
        // Consulta A para "google.com" con Transaction ID = 0x1234
        byte[] query = new byte[]
        {
            0x12, 0x34,             // Transaction ID
            0x01, 0x00,             // Flags: Standard query, RD=1
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x00,             // ANCOUNT = 0
            0x00, 0x00,             // NSCOUNT = 0
            0x00, 0x00,             // ARCOUNT = 0
            // Question: 6google3com0
            0x06, 0x67, 0x6f, 0x6f, 0x67, 0x6c, 0x65,
            0x03, 0x63, 0x6f, 0x6d,
            0x00,
            0x00, 0x01,             // QTYPE = A (1)
            0x00, 0x01              // QCLASS = IN (1)
        };

        var packet = DnsPacket.Parse(query, query.Length);

        Assert.NotNull(packet);
        Assert.Equal(0x1234, packet.TransactionId);
        Assert.True(packet.IsQuery);
        Assert.True(packet.RecursionDesired);
        Assert.Equal("google.com", packet.QuestionName);
        Assert.Equal(DnsPacket.TypeA, packet.QuestionType);
        Assert.Equal(1, packet.QuestionClass);
    }

    [Fact]
    public void CreateNxDomainResponse_FromQuery_GeneratesValidNxDomain()
    {
        byte[] query = new byte[]
        {
            0xAB, 0xCD,             // Transaction ID
            0x01, 0x00,             // Flags: Query, RD=1
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x00,             // ANCOUNT = 0
            0x00, 0x00,             // NSCOUNT = 0
            0x00, 0x00,             // ARCOUNT = 0
            // Question: 7blocked3com0
            0x07, 0x62, 0x6c, 0x6f, 0x63, 0x6b, 0x65, 0x64,
            0x03, 0x63, 0x6f, 0x6d,
            0x00,
            0x00, 0x01,             // QTYPE = A (1)
            0x00, 0x01              // QCLASS = IN (1)
        };

        byte[] response = DnsPacket.CreateNxDomainResponse(query, query.Length);

        Assert.NotEmpty(response);
        Assert.Equal(0xAB, response[0]);
        Assert.Equal(0xCD, response[1]);

        // Byte 2: QR=1 (0x80) | RD=1 (0x01) -> 0x81
        Assert.Equal(0x81, response[2]);
        // Byte 3: RA=1 (0x80) | RCODE=3 (0x03) -> 0x83
        Assert.Equal(0x83, response[3]);

        // QDCount = 1
        Assert.Equal(0x00, response[4]);
        Assert.Equal(0x01, response[5]);

        // ANCount = 0
        Assert.Equal(0x00, response[6]);
        Assert.Equal(0x00, response[7]);
    }

    [Fact]
    public void Parse_ResponseWithAnswers_ExtractsIpsAndTtl()
    {
        // Respuesta con 1 respuesta A para 142.250.190.46 con TTL = 300
        byte[] response = new byte[]
        {
            0x12, 0x34,             // Transaction ID
            0x81, 0x80,             // Flags: Response, RD=1, RA=1, RCODE=0
            0x00, 0x01,             // QDCOUNT = 1
            0x00, 0x01,             // ANCOUNT = 1
            0x00, 0x00,             // NSCOUNT = 0
            0x00, 0x00,             // ARCOUNT = 0
            // Question: 6google3com0
            0x06, 0x67, 0x6f, 0x6f, 0x67, 0x6c, 0x65,
            0x03, 0x63, 0x6f, 0x6d,
            0x00,
            0x00, 0x01, 0x00, 0x01, // QTYPE=A, QCLASS=IN
            // Answer: Puntero a google.com (0xC00C)
            0xC0, 0x0C,
            0x00, 0x01,             // TYPE = A
            0x00, 0x01,             // CLASS = IN
            0x00, 0x00, 0x01, 0x2C, // TTL = 300
            0x00, 0x04,             // RDLENGTH = 4
            142, 250, 190, 46       // IP Address: 142.250.190.46
        };

        var packet = DnsPacket.Parse(response, response.Length);

        Assert.NotNull(packet);
        Assert.False(packet.IsQuery);
        Assert.Single(packet.Answers);
        Assert.Equal(IPAddress.Parse("142.250.190.46"), packet.Answers[0].IpAddress);
        Assert.Equal(300u, packet.Answers[0].Ttl);
    }
}
