using AprsNet.Packets;

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
    [InlineData("", AprsValidationErrorCode.CallsignInvalidLength, null)]
    [InlineData("-9", AprsValidationErrorCode.CallsignInvalidLength, null)]
    [InlineData("N0CALLTOOLONG", AprsValidationErrorCode.CallsignInvalidLength, null)]
    [InlineData("N0 CALL", AprsValidationErrorCode.CallsignInvalidCharacter, 2)]
    [InlineData("N0CALL ", AprsValidationErrorCode.CallsignInvalidCharacter, 6)]
    [InlineData("N0/CALL", AprsValidationErrorCode.CallsignInvalidCharacter, 2)]
    [InlineData("NÖCALL", AprsValidationErrorCode.CallsignInvalidCharacter, 1)]
    public void RejectsCallsignsThatAreNotLettersAndDigits(string callsign, AprsValidationErrorCode code, int? index)
    {
        var ex = Assert.Throws<AprsValidationException>(() => AprsIsPasscode.Compute(callsign));
        Assert.Equal("callsign", ex.ParamName);
        Assert.Equal(code, ex.Code);
        Assert.Equal(index, ex.Index);
    }

    [Fact]
    public void RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => AprsIsPasscode.Compute(null!));
    }
}
