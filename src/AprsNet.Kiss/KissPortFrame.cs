using AprsNet.Ax25;

namespace AprsNet.Kiss;

/// <summary>An AX.25 frame a TNC passed on, with the radio port it was heard on.</summary>
/// <param name="Port">The TNC's radio port, 0 to 15: 0 on a single-port TNC.</param>
/// <param name="Frame">The frame.</param>
public readonly record struct KissPortFrame(int Port, Ax25Frame Frame);
