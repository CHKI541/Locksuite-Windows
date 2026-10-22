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
    // Sintaxis nueva (sesion 9)
    [InlineData("=google.com", true)]                      // host exacto
    [InlineData("=www.example.co.il", true)]
    [InlineData("lh*.googleusercontent.com", true)]        // comodin en la primera etiqueta
    [InlineData("*-docs.googleusercontent.com", true)]
    [InlineData("ci*.googleusercontent.com", true)]
    [InlineData("=", false)]
    [InlineData("=com", false)]                            // exacto sobre un TLD
    [InlineData("==google.com", false)]
    [InlineData("=*.google.com", false)]                   // '=' no admite comodines
    [InlineData("*google.com", false)]                     // comodin de etiqueta sobre un solo nivel
    [InlineData("ci*.com", false)]                         // idem
    [InlineData("**.google.com", false)]                   // etiqueta solo de comodines
    [InlineData("*.lh*.google.com", false)]                // "*." + comodin de etiqueta
    [InlineData(".lh*.google.com", false)]
    [InlineData("lh*.*.google.com", false)]                // comodin en dos etiquetas
    [InlineData("-lh*.google.com", false)]                 // etiqueta empieza con '-'
    [InlineData("lh*-.google.com", false)]                 // etiqueta termina con '-'
    public void IsValidPattern_Cases(string pattern, bool expected)
        => Assert.Equal(expected, DomainMatcher.IsValidPattern(pattern));

    [Theory]
    [InlineData("=google.com", "google.com", true)]
    [InlineData("=google.com", "GOOGLE.COM.", true)]
    [InlineData("=google.com", "www.google.com", false)]      // exacto: ningun subdominio
    [InlineData("=google.com", "notgoogle.com", false)]
    [InlineData("=mail.google.com", "mail.google.com", true)]
    [InlineData("=mail.google.com", "x.mail.google.com", false)]
    public void ExactPattern_Cases(string pattern, string host, bool expected)
        => Assert.Equal(expected, DomainMatcher.PatternMatches(pattern, host));

    [Theory]
    [InlineData("lh*.googleusercontent.com", "lh3.googleusercontent.com", true)]
    [InlineData("lh*.googleusercontent.com", "lh7-rt.googleusercontent.com", true)]
    [InlineData("lh*.googleusercontent.com", "LH5.GoogleUserContent.com", true)]
    [InlineData("lh*.googleusercontent.com", "play-lh.googleusercontent.com", false)]   // no es substring
    [InlineData("lh*.googleusercontent.com", "a.lh3.googleusercontent.com", false)]     // nunca cruza un punto
    [InlineData("lh*.googleusercontent.com", "googleusercontent.com", false)]
    [InlineData("lh*.googleusercontent.com", "lh3.googleusercontent.com.evil.com", false)]
    [InlineData("lh*.googleusercontent.com", "lh3.evilgoogleusercontent.com", false)]
    [InlineData("*-docs.googleusercontent.com", "doc-0s-3c-docs.googleusercontent.com", true)]
    [InlineData("*-docs.googleusercontent.com", "docs.googleusercontent.com", false)]
    [InlineData("*-docs.googleusercontent.com", "x.doc-0s-docs.googleusercontent.com", false)]
    [InlineData("ci*.googleusercontent.com", "ci3.googleusercontent.com", true)]
    [InlineData("ci*.googleusercontent.com", "blogger.googleusercontent.com", false)]
    [InlineData("*-aiplatform.googleapis.com", "us-central1-aiplatform.googleapis.com", true)]
    [InlineData("*-aiplatform.googleapis.com", "aiplatform.googleapis.com", false)]
    public void LabelGlobPattern_Cases(string pattern, string host, bool expected)
        => Assert.Equal(expected, DomainMatcher.PatternMatches(pattern, host));

    [Fact]
    public void Blocklist_HasPriorityOverWhitelist()
    {
        var allow = new[] { "google.com", "clients6.google.com" };
        var block = new[] { "www.google.com", "=google.com", "appsgenaiserver-pa.clients6.google.com" };

        Assert.True(DomainMatcher.IsAllowed(allow, "mail.google.com", block));
        Assert.True(DomainMatcher.IsAllowed(allow, "people-pa.clients6.google.com", block));
        Assert.False(DomainMatcher.IsAllowed(allow, "www.google.com", block));
        Assert.False(DomainMatcher.IsAllowed(allow, "google.com", block));
        Assert.False(DomainMatcher.IsAllowed(allow, "appsgenaiserver-pa.clients6.google.com", block));
        Assert.False(DomainMatcher.IsAllowed(allow, "facebook.com", block));       // ni permitido ni bloqueado
        Assert.True(DomainMatcher.IsAllowed(allow, "mail.google.com", null));      // sin lista de bloqueo
    }

    [Fact]
    public void MatchesAny_IgnoresNullAndEmpty()
    {
        Assert.False(DomainMatcher.MatchesAny(null, "google.com"));
        Assert.False(DomainMatcher.MatchesAny(new[] { "google.com" }, null));
        Assert.False(DomainMatcher.MatchesAny(new[] { "google.com" }, "   "));
        Assert.False(DomainMatcher.MatchesAny(new[] { "", "   " }, "google.com"));
    }
}
