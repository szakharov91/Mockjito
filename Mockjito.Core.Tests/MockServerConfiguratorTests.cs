using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.OpenApi;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

namespace Mockjito.Core.Tests;

public sealed class MockServerConfiguratorTests
{
    private readonly MockServerConfigurator sut = new();

    [Fact]
    public void Build_one_path_multiple_methods_produces_multiple_routes()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.String }),
                },
            },
        });
        pathItem.AddOperation(HttpMethod.Post, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["201"] = new OpenApiResponse
                {
                    Description = "Created",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.Integer }),
                },
            },
        });
        doc.Paths.Add("/items", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        routes.Should().HaveCount(2);
        routes.Select(r => r.Method).Should().BeEquivalentTo(new[] { HttpMethod.Get, HttpMethod.Post });
    }

    [Fact]
    public void Build_merges_pathItem_and_operation_parameters()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "fromPath",
                    In = ParameterLocation.Query,
                    Required = false,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                },
            ]
        };

        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Parameters =
            [
                new OpenApiParameter
                {
                    Name = "fromOp",
                    In = ParameterLocation.Header,
                    Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                },
            ],
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse { Description = "OK" },
            },
        });
        doc.Paths.Add("/x", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        IReadOnlyList<OpenApiParameterInfo> p = routes[0].Parameters;
        p.Should().HaveCount(2);
        p[0].Name.Should().Be("fromPath");
        p[0].In.Should().Be(ParameterLocation.Query);
        p[1].Name.Should().Be("fromOp");
        p[1].In.Should().Be(ParameterLocation.Header);
        p[1].Required.Should().BeTrue();
    }

    [Fact]
    public void Build_prefers_application_json_schema_when_present()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["text/plain"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.String },
                        },
                        ["application/json"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Integer },
                        },
                    },
                },
            },
        });
        doc.Paths.Add("/a", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        IOpenApiSchema? schema = routes[0].Responses[200];
        schema.Should().NotBeNull();
        schema!.Type.Should().Be(JsonSchemaType.Integer);
    }

    [Fact]
    public void Build_falls_back_to_first_media_with_schema_when_no_json()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content = new Dictionary<string, OpenApiMediaType>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["text/plain"] = new OpenApiMediaType
                        {
                            Schema = new OpenApiSchema { Type = JsonSchemaType.Boolean },
                        },
                    },
                },
            },
        });
        doc.Paths.Add("/b", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        routes[0].Responses[200]!.Type.Should().Be(JsonSchemaType.Boolean);
    }

    [Fact]
    public void Build_default_response_maps_to_200_only_when_no_2xx()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["default"] = new OpenApiResponse
                {
                    Description = "def",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.String }),
                },
            },
        });
        doc.Paths.Add("/c", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        routes[0].Responses.Should().ContainKey(200);
        routes[0].Responses[200]!.Type.Should().Be(JsonSchemaType.String);
    }

    [Fact]
    public void Build_default_response_not_mapped_when_2xx_exists()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["201"] = new OpenApiResponse
                {
                    Description = "Created",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.Integer }),
                },
                ["default"] = new OpenApiResponse
                {
                    Description = "def",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.String }),
                },
            },
        });
        doc.Paths.Add("/d", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        routes[0].Responses.Should().NotContainKey(200);
        routes[0].Responses[201]!.Type.Should().Be(JsonSchemaType.Integer);
    }

    [Fact]
    public void Build_non_numeric_response_codes_are_ignored()
    {
        var doc = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "t", Version = "1" },
            Paths = new OpenApiPaths(),
        };

        var pathItem = new OpenApiPathItem();
        pathItem.AddOperation(HttpMethod.Get, new OpenApiOperation
        {
            Responses = new OpenApiResponses
            {
                ["2xx"] = new OpenApiResponse
                {
                    Description = "range",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.String }),
                },
                ["200"] = new OpenApiResponse
                {
                    Description = "OK",
                    Content = JsonContent(new OpenApiSchema { Type = JsonSchemaType.Boolean }),
                },
            },
        });
        doc.Paths.Add("/e", pathItem);

        IReadOnlyList<RouteDefinition> routes = sut.Build(doc);
        routes[0].Responses.Should().ContainKey(200);
        routes[0].Responses.Should().NotContainKey(0);
        routes[0].Responses[200]!.Type.Should().Be(JsonSchemaType.Boolean);
    }

    private static Dictionary<string, OpenApiMediaType> JsonContent(OpenApiSchema schema) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/json"] = new OpenApiMediaType { Schema = schema },
        };
}
