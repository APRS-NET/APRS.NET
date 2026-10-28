namespace AprsNet.Kiss;

/// <summary>A radio port an AGWPE server offers: see <see cref="AgwpeConnection.Ports"/>.</summary>
/// <param name="Number">
/// The port's number from 0, as <see cref="KissConnection.TncPort"/> and <see cref="KissPortFrame.Port"/> take it; the
/// server's own list says <c>Port1</c> for 0.
/// </param>
/// <param name="Description">How the server describes it, such as <c>first soundcard mono</c>; empty when it doesn't.</param>
public sealed record AgwpePort(int Number, string Description);
