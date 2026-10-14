namespace AprsNet.AprsIsTcpClient.Tests;

public class AprsIsPasscodeTests
{
    [Theory]
    [InlineData("N0CALL", 13023)] // the value published with the algorithm
    [InlineData("NZ0P", 3560)]
    [InlineData("W1AW", 25988)]
    [InlineData("K1A", 31187)] // odd length: the last character goes into the high byte alone
    [InlineData("A", 13026)]
    [InlineData("VK2ABC", 21931)]
    [InlineData("JA1ZZZ", 21155)]
    [InlineData("2E0ABC", 13221)] // digit-first prefix
    [InlineData("9M2PJU", 12970)]
    public void ComputesThePasscode(string callsign, int expected)
    {
        Assert.Equal(expected, AprsIsPasscode.Compute(callsign));
    }

    [Theory]
    [InlineData("n0call")]
    [InlineData("N0CALL-9")]
    [InlineData("n0Call-15")]
    [InlineData("N0CALL-")]
    [InlineData("N0CALL-ANYTHING")]
    public void IgnoresTheSsidAndLetterCase(string callsign)
    {
        Assert.Equal(13023, AprsIsPasscode.Compute(callsign));
    }

    [Theory]
    [InlineData("ZZZZZZZZZ")]
    [InlineData("999999999")]
    [InlineData("A1")]
    public void StaysWithinFifteenBitsAndNeverReturnsTheReadOnlyPasscode(string callsign)
    {
        Assert.InRange(AprsIsPasscode.Compute(callsign), 0, 0x7FFF);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-9")]
    [InlineData("N0CALLTOOLONG")]
    [InlineData("N0 CALL")]
    [InlineData("N0CALL ")]
    [InlineData("N0/CALL")]
    [InlineData("NÖCALL")]
    public void RejectsCallsignsThatAreNotLettersAndDigits(string callsign)
    {
        var ex = Assert.Throws<ArgumentException>(() => AprsIsPasscode.Compute(callsign));
        Assert.Equal("callsign", ex.ParamName);
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AprsIsPasscode.Compute(null!));
    }
}
