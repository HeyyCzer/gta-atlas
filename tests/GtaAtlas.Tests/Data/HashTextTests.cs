using GtaAtlas.Data;

namespace GtaAtlas.Tests.Data;

public class HashTextTests
{
    // velenogt, as shown by the game / plebmasters.
    private const uint Velenogt = 3316510914;

    [Theory]
    [InlineData("3316510914")]
    [InlineData("-978456382")]
    [InlineData("0xC5ADF0C2")]
    [InlineData("0xc5adf0c2")]
    [InlineData("C5ADF0C2")]
    [InlineData("  0xC5ADF0C2 ")]
    public void Parses_every_notation(string text)
    {
        Assert.True(HashText.TryParse(text, out var hash));
        Assert.Equal(Velenogt, hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bench")]
    [InlineData("facade")] // hex letters, but not 8 digits
    [InlineData("99999999999")]
    [InlineData("0xZZ")]
    public void Rejects_non_hashes(string? text) => Assert.False(HashText.TryParse(text, out _));

    [Fact]
    public void Formats_signed_and_hex()
    {
        Assert.Equal("-978456382", HashText.ToSigned(Velenogt));
        Assert.Equal("0xC5ADF0C2", HashText.ToHex(Velenogt));
    }
}
