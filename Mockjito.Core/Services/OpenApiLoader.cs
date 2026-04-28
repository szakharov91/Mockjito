using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Mockjito.Core.Services;

/// <summary>
/// Loads an OpenAPI document from a local file (JSON/YAML).
/// </summary>
public sealed class OpenApiLoader
{
    /// <summary>
    /// Asynchronously reads a specification from disk.
    /// </summary>
    /// <param name="filePath">Path to a .json, .yaml, or .yml file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Parsed OpenAPI document.</returns>
    /// <exception cref="InvalidOperationException">Unsupported extension, empty paths, or read errors.</exception>
    public async Task<OpenApiDocument> LoadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var format = extension switch
        {
            ".json" => OpenApiConstants.Json,
            ".yaml" => OpenApiConstants.Yaml,
            ".yml" => OpenApiConstants.Yml,
            _ => throw new InvalidOperationException(
                $"Unsupported file extension '{extension}'. Expected: .json, .yaml, .yml."),
        };

        var settings = new OpenApiReaderSettings
        {
            LoadExternalRefs = false,
        };

        // Registers YAML reader for this settings instance (requires Microsoft.OpenApi.YamlReader package).
        settings.AddYamlReader();

        await using var stream = File.OpenRead(filePath);
        var (document, diagnostic) = await OpenApiDocument.LoadAsync(stream, format, settings, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            throw new InvalidOperationException("Failed to read OpenAPI document (empty result).");
        }

        if (diagnostic is not null && diagnostic.Errors.Count > 0)
        {
            var first = diagnostic.Errors[0];
            throw new InvalidOperationException($"OpenAPI read error: {first.Message}");
        }

        if (document.Paths is null || document.Paths.Count == 0)
        {
            throw new InvalidOperationException(
                "The document is missing the 'paths' section, or it is empty. Add at least one path.");
        }

        return document;
    }
}
