using System.Text;
using LupiraDavApi.Auth;
using Xunit;

namespace LupiraDavApi.UnitTests;

/// <summary>Basic-credential decoding + LDAP hardening, ported from LupiraCalApi's proven handler.</summary>
public sealed class BasicCredentialsTests
{
    private static string Header(string raw) => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));

    [Fact]
    public void Decodes_and_lowercases_the_email_splitting_on_the_first_colon()
    {
        Assert.True(DavBasicAuthHandler.TryParseBasicCredentials(Header("Anna@X.SE:p:a:ss"), out var email, out var password));
        Assert.Equal("anna@x.se", email);
        Assert.Equal("p:a:ss", password);
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("Basic !!!not-base64!!!")]
    [InlineData("")]
    public void Rejects_malformed_headers(string header)
    {
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials(header, out _, out _));
    }

    [Theory]
    [InlineData("no-colon")]
    [InlineData(":only-password")]
    [InlineData("only-email:")]
    public void Rejects_missing_parts(string raw)
    {
        Assert.False(DavBasicAuthHandler.TryParseBasicCredentials(Header(raw), out _, out _));
    }

    [Fact]
    public void Ldap_filter_escaping_neutralises_injection_characters()
    {
        Assert.Equal(@"a\2ab\28c\29d\5ce", DavBasicAuthHandler.EscapeLdapFilter(@"a*b(c)d\e"));
    }

    [Fact]
    public void Ldap_uri_parses_host_and_port()
    {
        Assert.Equal(("authentik-ldap", 3389), DavBasicAuthHandler.ParseLdapUri("ldap://authentik-ldap:3389"));
        Assert.Equal(("ldap.example.com", 389), DavBasicAuthHandler.ParseLdapUri("ldap://ldap.example.com"));
    }
}
