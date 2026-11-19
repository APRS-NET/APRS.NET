using System.Globalization;
using AprsNet.AprsIsTcpClient.Is2;
using AprsNet.Packets.Is2;

namespace AprsNet.AprsIsTcpClient.Tests;

public class Is2MetaLineTests
{
    [Fact]
    public void WritesEveryValueWithTheKeysAprscReads()
    {
        var line = Is2MetaLine.Format(
            new ReceptionMetadata
            {
                RssiDbm = -97.5,
                SnrDb = 8.2,
                Modem = ReceptionModem.LoRaAprs,
                BitRate = 300,
                FrequencyHz = 433775000,
            }
        );

        Assert.Equal(
            "#is2-meta rx_rssi_dbm=-97.5 rx_snr_db=8.2 rx_modem=2 rx_bitrate=300 rx_frequency=433775000",
            line
        );
    }

    [Fact]
    public void WritesOnlyTheValuesGiven()
    {
        var line = Is2MetaLine.Format(
            new ReceptionMetadata
            {
                Modem = ReceptionModem.Ax25,
                BitRate = 1200,
                FrequencyHz = 144390000,
            }
        );

        Assert.Equal("#is2-meta rx_modem=1 rx_bitrate=1200 rx_frequency=144390000", line);
    }

    [Fact]
    public void WritesADecimalPointWhateverTheCulture()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var line = Is2MetaLine.Format(new ReceptionMetadata { RssiDbm = -97.5, FrequencyHz = 144390000.5 });

            Assert.Equal("#is2-meta rx_rssi_dbm=-97.5 rx_frequency=144390000.5", line);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void NeverWritesAnExponent()
    {
        var line = Is2MetaLine.Format(new ReceptionMetadata { FrequencyHz = 10_000_000_000_000_000, SnrDb = 0.0001 });

        Assert.Equal("#is2-meta rx_snr_db=0.0001 rx_frequency=10000000000000000", line);
    }

    public static TheoryData<ReceptionMetadata> Unusable =>
        [
            new ReceptionMetadata { RssiDbm = double.NaN },
            new ReceptionMetadata { SnrDb = double.PositiveInfinity },
            new ReceptionMetadata { BitRate = 0 },
            new ReceptionMetadata { BitRate = -1200 },
            new ReceptionMetadata { FrequencyHz = 0 },
            new ReceptionMetadata { FrequencyHz = -144390000 },
            new ReceptionMetadata { FrequencyHz = double.NaN },
            new ReceptionMetadata { Modem = ReceptionModem.Undefined },
            new ReceptionMetadata { Modem = (ReceptionModem)1025 },
            new ReceptionMetadata { Modem = (ReceptionModem)(-1) },
        ];

    [Theory]
    [MemberData(nameof(Unusable))]
    public void LeavesOutAValueTheServerWouldIgnore(ReceptionMetadata reception)
    {
        Assert.Null(Is2MetaLine.Format(reception));
    }

    [Fact]
    public void KeepsTheUsableValuesWhenOneIsNot()
    {
        var line = Is2MetaLine.Format(new ReceptionMetadata { RssiDbm = double.NaN, BitRate = 1200 });

        Assert.Equal("#is2-meta rx_bitrate=1200", line);
    }

    [Fact]
    public void PassesOnAModemNumberAprscAddsLater()
    {
        Assert.Equal(
            "#is2-meta rx_modem=1024",
            Is2MetaLine.Format(new ReceptionMetadata { Modem = (ReceptionModem)1024 })
        );
    }

    [Fact]
    public void WritesNoLineForNoMetadata()
    {
        Assert.Null(Is2MetaLine.Format(null));
        Assert.Null(Is2MetaLine.Format(new ReceptionMetadata()));
    }
}
