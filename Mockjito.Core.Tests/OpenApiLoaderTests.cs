using FluentAssertions;
using Microsoft.OpenApi;
using Mockjito.Core.Services;

namespace Mockjito.Core.Tests;

public sealed class OpenApiLoaderTests
{
    private static string ResourcePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", fileName);

    [Fact]
    public async Task LoadAsync_json_ok()
    {
        var loader = new OpenApiLoader();
        OpenApiDocument doc = await loader.LoadAsync(ResourcePath("minimal.json"));
        doc.Paths.Should().NotBeNull();
        doc.Paths!.Should().ContainKey("/ping");
    }

    [Fact]
    public async Task LoadAsync_yaml_ok()
    {
        var loader = new OpenApiLoader();
        OpenApiDocument doc = await loader.LoadAsync(ResourcePath("minimal.yaml"));
        doc.Paths.Should().NotBeNull();
        doc.Paths!.Should().ContainKey("/ping");
    }

    [Fact]
    public async Task LoadAsync_unsupported_extension_throws()
    {
        var loader = new OpenApiLoader();
        Func<Task<OpenApiDocument>> act = async () => await loader.LoadAsync(ResourcePath("invalid.txt"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unsupported file extension*");
    }

    [Fact]
    public async Task LoadAsync_empty_paths_throws()
    {
        var loader = new OpenApiLoader();
        Func<Task<OpenApiDocument>> act = async () => await loader.LoadAsync(ResourcePath("empty-paths.json"));
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*paths*");
    }

    /// <summary>
    /// Loader always uses <c>LoadExternalRefs = false</c>; file <c>$ref</c> must stay unresolved.
    /// </summary>
    [Fact]
    public async Task LoadAsync_external_file_ref_not_resolved()
    {
        var loader = new OpenApiLoader();
        OpenApiDocument doc = await loader.LoadAsync(ResourcePath("external-main.json"));
        IOpenApiPathItem pathItem = doc.Paths!["/refd"];
        OpenApiOperation op = pathItem.Operations![HttpMethod.Get];
        IOpenApiSchema? schema = op.Responses!["200"].Content!["application/json"].Schema;
        schema.Should().NotBeNull();
        var holder = schema as IOpenApiReferenceHolder;
        holder.Should().NotBeNull("response schema should be a reference holder for file $ref");
        holder!.UnresolvedReference.Should().BeTrue("external $ref must not be loaded when LoadExternalRefs is false");
    }
}
