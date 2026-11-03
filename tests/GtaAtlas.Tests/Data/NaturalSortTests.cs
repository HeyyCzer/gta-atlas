using GtaAtlas.Data;

namespace GtaAtlas.Tests.Data;

public class NaturalSortTests
{
    [Theory]
    [InlineData("C/0/2/F", "C/0/10/F")]
    [InlineData("P/7/8/M", "P/7/12/M")]
    [InlineData("prop_bench_01a", "prop_bench_2")]
    [InlineData("mp2025_02", "mp2026_01")]
    [InlineData("abc", "abc1")]
    public void Orders_numbers_by_value(string lower, string higher) =>
        Assert.True(string.CompareOrdinal(NaturalSort.Key(lower), NaturalSort.Key(higher)) < 0);

    [Fact]
    public void Pads_digit_runs()
    {
        Assert.Equal("C/0000000000/0000000010/F", NaturalSort.Key("C/0/10/F"));
        Assert.Equal("4294967295", NaturalSort.Key("4294967295"));
    }

    [Fact]
    public void Keeps_names_without_digits() => Assert.Equal("bench", NaturalSort.Key("bench"));
}
