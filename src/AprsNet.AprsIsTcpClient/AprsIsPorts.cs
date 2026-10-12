namespace AprsNet.AprsIsTcpClient;

/// <summary>Well-known APRS-IS server ports. Servers may offer others; see the server's status page on <see cref="StatusPage"/>.</summary>
public static class AprsIsPorts
{
    /// <summary>User-defined filter port. The standard port for clients.</summary>
    public const int UserDefinedFilter = 14580;

    /// <summary>Full feed port. Delivers all traffic; heavy, and most clients should not use it.</summary>
    public const int FullFeed = 10152;

    /// <summary>HTML status page listing the ports a server offers.</summary>
    public const int StatusPage = 14501;
}
