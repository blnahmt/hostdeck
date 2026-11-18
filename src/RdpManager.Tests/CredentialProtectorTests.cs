using RdpManager.Security;
using Xunit;

namespace RdpManager.Tests;

public class CredentialProtectorTests
{
    private readonly CredentialProtector _protector = new();

    [Fact]
    public void Protect_then_unprotect_returns_original()
    {
        const string plaintext = "P@ssw0rd! ustu turkce cikabilir: sifre";

        var blob = _protector.Protect(plaintext);

        Assert.NotNull(blob);
        Assert.NotEqual(plaintext, blob);
        Assert.Equal(plaintext, _protector.Unprotect(blob));
    }

    [Fact]
    public void Protected_blob_does_not_contain_plaintext()
    {
        const string plaintext = "CokGizliParola123";

        var blob = _protector.Protect(plaintext)!;

        Assert.DoesNotContain(plaintext, blob, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_input_yields_null(string? input)
    {
        Assert.Null(_protector.Protect(input));
        Assert.Null(_protector.Unprotect(input));
    }

    [Fact]
    public void Corrupt_blob_returns_null_instead_of_throwing()
    {
        // Baska bir Windows kullanicisinda kaydedilmis kaydin taklidi.
        Assert.Null(_protector.Unprotect("bu-gecerli-bir-dpapi-blobu-degil"));
    }
}
