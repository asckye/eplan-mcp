using EplanMcp.Contracts.Security;

namespace EplanMcp.Contracts.Tests.Security;

[Trait("Size", "Small")]
public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("password")]
    [InlineData("dbPassWord")]
    [InlineData("PASSWD")]
    [InlineData("client_secret")]
    [InlineData("accessToken")]
    [InlineData("Credentials")]
    [InlineData("HTTP_AUTHORIZATION")]
    [InlineData("apiKey")]
    [InlineData("KEY")]
    public void Redact_SensitiveKey_ReplacesTheWholeValue(string key)
    {
        Assert.True(SecretRedactor.IsSensitiveKey(key));
        Assert.Equal(SecretRedactor.Placeholder, SecretRedactor.Redact("synthetic-credential", key));
        Assert.Equal(SecretRedactor.Placeholder, SecretRedactor.Redact(string.Empty, key));
    }

    [Theory]
    [InlineData("page")]
    [InlineData("project")]
    [InlineData("keyDescription")]
    [InlineData("count")]
    [InlineData("")]
    public void Redact_OrdinaryKey_KeepsTheValue(string key)
    {
        Assert.False(SecretRedactor.IsSensitiveKey(key));
        Assert.Equal("ordinary text", SecretRedactor.Redact("ordinary text", key));
    }

    [Theory]
    [InlineData("Authorization: Bearer synthetic-token.123==", "Authorization: Bearer [REDACTED]")]
    [InlineData("basic c3ludGhldGljOnBhc3M=", "basic [REDACTED]")]
    [InlineData("Basic dXNlcjpwYXNz", "Basic [REDACTED]")]
    [InlineData("bearer\tsynthetic_token", "bearer [REDACTED]")]
    [InlineData("connect https://synthetic-user:synthetic-password@example.test:443/path?a=1#b", "connect https://[REDACTED]@example.test:443/path?a=1#b")]
    [InlineData("ssh://synthetic-user@example.test/path", "ssh://[REDACTED]@example.test/path")]
    [InlineData("https://user%40example.test:p%40ss@example.test/", "https://[REDACTED]@example.test/")]
    [InlineData("first -----BEGIN PRIVATE KEY-----\nsynthetic-key\n-----END PRIVATE KEY----- last", "first [REDACTED] last")]
    [InlineData("-----BEGIN RSA PRIVATE KEY-----\r\nsynthetic-key\r\n-----END RSA PRIVATE KEY-----", "[REDACTED]")]
    [InlineData("-----BEGIN CERTIFICATE-----\nsynthetic-certificate\n-----END CERTIFICATE-----", "[REDACTED]")]
    [InlineData("password=synthetic-password; mode=normal", "password=[REDACTED]; mode=normal")]
    [InlineData("PASSWD = \"synthetic password\"", "PASSWD = [REDACTED]")]
    [InlineData("client_secret='synthetic secret'", "client_secret=[REDACTED]")]
    [InlineData("accessToken=synthetic-token&size=2", "accessToken=[REDACTED]&size=2")]
    [InlineData("credential=synthetic-credential, count=2", "credential=[REDACTED], count=2")]
    [InlineData("authorization=synthetic-credential", "authorization=[REDACTED]")]
    [InlineData("apiKey=synthetic-key", "apiKey=[REDACTED]")]
    [InlineData("password=\"synthetic \\\"quoted\\\" password\"", "password=[REDACTED]")]
    [InlineData("Bearer first-token then Basic cDpz", "Bearer [REDACTED] then Basic [REDACTED]")]
    [InlineData("message=\"password=synthetic-secret\"", "message=\"password=[REDACTED]\"")]
    [InlineData("note='secret=synthetic-secret'", "note='secret=[REDACTED]'")]
    [InlineData("message=\"password=first-secret; token=second-secret\"", "message=\"password=[REDACTED]; token=[REDACTED]\"")]
    [InlineData("password=\"message=synthetic-secret\"; note=normal", "password=[REDACTED]; note=normal")]
    public void Redact_CredentialText_ReplacesOnlyCredentials(string text, string expected)
    {
        string result = SecretRedactor.Redact(text);

        Assert.Equal(expected, result);
        Assert.Equal(result, SecretRedactor.Redact(result));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Create page +A1/2; count=4")]
    [InlineData("The basic circuit has a token count and no password.")]
    [InlineData("https://example.test/a?name=value#fragment")]
    [InlineData("Contact synthetic-user@example.test")]
    [InlineData("keyDescription=visible; mode=normal")]
    [InlineData("Bearer")]
    [InlineData("Basic")]
    [InlineData("Basic text")]
    [InlineData("Basic KDpz")]
    [InlineData("Unrelated -----BEGIN TEXT----- marker")]
    public void Redact_OrdinaryText_IsUnchanged(string text)
    {
        Assert.Equal(text, SecretRedactor.Redact(text));
    }
}
