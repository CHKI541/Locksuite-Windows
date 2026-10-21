using LockSuite.Core;
using Xunit;

namespace LockSuite.Core.Tests;

public class DomainMatcherTests
{
    private static readonly string[] Wl =
        { "google.com", "*.gstatic.com", "mercadopago.com.ar", "prog.co.il" };

    [Theory]
    [InlineData("google.com", true)]            // bare exacto
    [InlineData("www.google.com", true)]        // bare subdominio
    [InlineData("mail.google.com", true)]       // subdominio profundo
    [InlineData("a.b.c.google.com", true)]      // muy profundo
    [InlineData("google.com.ar", false)]        // sufijo distinto
    [InlineData("notgoogle.com", false)]        // substring
    [InlineData("evilgoogle.com", false)]       // prefijo pegado
    [InlineData("ssl.gstatic.com", true)]       // wildcard subdominio
    [InlineData("gstatic.com", false)]          // *.x no matchea x
    [InlineData("GOOGLE.COM", true)]            // case-insensitive
    [InlineData("www.google.com.", true)]       // punto final FQDN
    [InlineData("mercadopago.com.ar", true)]    // doble sufijo exacto
    [InlineData("api.mercadopago.com.ar", true)]// doble sufijo + sub
    [InlineData("prog.co.il", true)]
    [InlineData("", false)]
    [InlineData("com", false)]
    [InlineData("google.com.evil.com", false)]  // dominio permitido en el medio
    public void IsAllowed_Cases(string host, bool expected)
        => Assert.Equal(expected, DomainMatcher.IsAllowed(Wl, host));

    [Fact]
    public void EmptyWhitelist_DeniesEverything()
        => Assert.False(DomainMatcher.IsAllowed(System.Array.Empty<string>(), "google.com"));

    [Fact]
    public void ListingSubdomain_DoesNotOpenParent()
    {
        var wl = new[] { "mail.google.com" };
        Assert.True(DomainMatcher.IsAllowed(wl, "mail.google.com"));
        Assert.False(DomainMatcher.IsAllowed(wl, "google.com"));
        Assert.False(DomainMatcher.IsAllowed(wl, "drive.google.com"));
    }

    [Theory]
    [InlineData("google.com", true)]
    [InlineData("*.gstatic.com", true)]
    [InlineData(".example.com", true)]
    [InlineData("sub.example.co.il", true)]
    [InlineData("xn--bcher-kva.com", true)]   // punycode (IDN) valido
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("com", false)]                 // TLD suelto -> abriria demasiado
    [InlineData("localhost", false)]           // single-label
    [InlineData("*", false)]
    [InlineData("*.com", false)]               // comodin sobre TLD
    [InlineData("a.*.com", false)]             // comodin en el medio
    [InlineData("exa mple.com", false)]        // espacio
    [InlineData("http://example.com", false)]  // esquema / ':' / '/'
    [InlineData("a..b.com", false)]            // label vacio
    [InlineData("-bad.com", false)]            // label empieza con '-'
    [InlineData("bad-.com", false)]            // label termina con '-'
    public void IsValidPattern_Cases(string pattern, bool expected)
        => Assert.Equal(expected, DomainMatcher.IsValidPattern(pattern));
}
