namespace Mockjito.Core.Configuration;

/// <summary>
/// Mock server options from configuration and CLI.
/// </summary>
public sealed class MockServerOptions
{
    /// <summary>
    /// Verbose logging (request/response bodies).
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// HTTP server port.
    /// </summary>
    public int Port { get; set; } = 5000;
}
