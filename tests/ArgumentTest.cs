using System;
using Xunit;

namespace Kent.DbCli.Tests;

public class ArgumentTest
{
    [Theory]
    [InlineData("--cowabunga", null)]
    [InlineData("", null)]
    [InlineData("bunga", "bunga")]
    public void ParseStringArgument(string value, string? expected)
    {
        var arg = new Argument<string>("unnamed");
        var parsed = arg.Parse(value);

        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("--cowabunga", true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ParseBooleanArgument(string value, bool expected)
    {
        var arg = new Argument<bool>("unnamed");
        var parsed = arg.Parse(value);

        Assert.Equal(expected, parsed);
    }

    [Fact]
    public void GetFromStringArgs()
    {
        var args = new[] {"--yee", "--boi", "--cowabunga", "420"};
        var x = new Argument<bool>("--yee");
        var y = new Argument<int>("--cowabunga");
        var z = new Argument<bool>("--hello");

        Assert.True(x.GetOrDefault(args));
        Assert.Equal(420, y.GetOrDefault(args));
        Assert.False(z.GetOrDefault(args));
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("1b", 1)]
    [InlineData("1B", 1)]
    [InlineData("1K", 1_000)]
    [InlineData("1M", 1_000_000)]
    public void ParsesBytes(string value, int expected)
    {
        var arg = new Argument<Bytes>("--bytes");
        var parsed = arg.Parse(value);

        Assert.NotNull(parsed);
        Assert.Equal(expected, parsed.Value);
    }
}
