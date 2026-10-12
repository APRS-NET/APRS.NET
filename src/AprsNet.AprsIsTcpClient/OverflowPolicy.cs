namespace AprsNet.AprsIsTcpClient;

/// <summary>What the client does when its bounded receive queue is full.</summary>
public enum OverflowPolicy
{
    /// <summary>Stop reading from the socket until the consumer catches up. Lossless; the server may drop a slow client.</summary>
    Block,

    /// <summary>Discard the oldest queued line to make room for the newest.</summary>
    DropOldest,
}
