using System;
using System.Collections.Generic;
using System.Text;
using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

public class SniParserTests
{
    // Construye un ClientHello minimo con una extension SNI para el host dado.
    private static byte[] BuildClientHello(string hostname)
    {
        var host = Encoding.ASCII.GetBytes(hostname);
        var b = new List<byte>();

        // server_name entry: type host_name(0) + name_len(2) + name
        var entry = new List<byte> { 0x00 };
        entry.Add((byte)(host.Length >> 8)); entry.Add((byte)(host.Length & 0xff));
        entry.AddRange(host);

        // server_name_list: list_len(2) + entry
        var list = new List<byte> { (byte)(entry.Count >> 8), (byte)(entry.Count & 0xff) };
        list.AddRange(entry);

        // extension: type(0x0000) + ext_len(2) + list
        var ext = new List<byte> { 0x00, 0x00, (byte)(list.Count >> 8), (byte)(list.Count & 0xff) };
        ext.AddRange(list);

        // ClientHello body
        var body = new List<byte>();
        body.AddRange(new byte[] { 0x03, 0x03 });        // client_version
        body.AddRange(new byte[32]);                     // random
        body.Add(0x00);                                  // session_id_len = 0
        body.AddRange(new byte[] { 0x00, 0x02, 0x13, 0x01 }); // cipher_suites (len 2)
        body.AddRange(new byte[] { 0x01, 0x00 });        // compression (len 1, null)
        body.Add((byte)(ext.Count >> 8)); body.Add((byte)(ext.Count & 0xff)); // extensions_len
        body.AddRange(ext);

        // Handshake: type(1) + len(3)
        var hs = new List<byte> { 0x01,
            (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)(body.Count & 0xff) };
        hs.AddRange(body);

        // Record: type(0x16) + version(2) + len(2)
        var rec = new List<byte> { 0x16, 0x03, 0x01,
            (byte)(hs.Count >> 8), (byte)(hs.Count & 0xff) };
        rec.AddRange(hs);
        return rec.ToArray();
    }

    [Theory]
    [InlineData("mitmachim.top")]
    [InlineData("www.google.com")]
    [InlineData("prog.co.il")]
    [InlineData("mail.google.com")]
    public void ExtractsSni(string host)
        => Assert.Equal(host, SniParser.TryParse(BuildClientHello(host)));

    [Fact]
    public void Truncated_ReturnsNull()
        => Assert.Null(SniParser.TryParse(new byte[] { 0x16, 0x03, 0x01, 0x00, 0x01, 0x01 }));

    [Fact]
    public void Empty_ReturnsNull()
        => Assert.Null(SniParser.TryParse(Array.Empty<byte>()));

    [Fact]
    public void NotHandshake_ReturnsNull()
        => Assert.Null(SniParser.TryParse(new byte[] { 0x17, 0, 0, 0, 0, 0 }));
}
