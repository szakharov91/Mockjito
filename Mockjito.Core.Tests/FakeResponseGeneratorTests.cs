using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.OpenApi;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

namespace Mockjito.Core.Tests;

public sealed class FakeResponseGeneratorTests
{
    private readonly FakeResponseGenerator sut = new();

    [Fact]
    public void Generate_null_schema_returns_empty_object()
    {
        sut.Generate(null).Should().BeOfType<JsonObject>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void Generate_string_without_format_uses_bogus_word()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String };
        JsonNode node = sut.Generate(schema);
        node.Should().BeAssignableTo<JsonValue>();
        node.AsValue().GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("email")]
    [InlineData("uuid")]
    [InlineData("date")]
    [InlineData("date-time")]
    [InlineData("uri")]
    [InlineData("url")]
    public void Generate_string_formats(string format)
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = format };
        string text = sut.Generate(schema).AsValue().GetValue<string>()!;
        text.Should().NotBeNullOrWhiteSpace();
        if (format == "uuid")
        {
            Guid.TryParse(text, out _).Should().BeTrue();
        }

        if (format == "email")
        {
            text.Should().Contain("@");
        }
    }

    [Fact]
    public void Generate_integer_respects_minimum_maximum()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Integer,
            Minimum = "10",
            Maximum = "12",
        };

        for (int i = 0; i < 30; i++)
        {
            int v = sut.Generate(schema).AsValue().GetValue<int>();
            v.Should().BeInRange(10, 12);
        }
    }

    [Fact]
    public void Generate_boolean_returns_bool()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Boolean };
        var kinds = new HashSet<JsonValueKind>();
        for (int i = 0; i < 20; i++)
        {
            JsonNode node = sut.Generate(schema);
            node.Should().BeAssignableTo<JsonValue>();
            kinds.Add(node.GetValueKind());
        }

        kinds.Should().Contain(JsonValueKind.True).And.Contain(JsonValueKind.False);
    }

    [Fact]
    public void Generate_array_has_between_1_and_3_items()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Array,
            Items = new OpenApiSchema { Type = JsonSchemaType.String },
        };

        for (int i = 0; i < 25; i++)
        {
            JsonArray arr = sut.Generate(schema).AsArray();
            arr.Should().NotBeEmpty();
            arr.Count.Should().BeInRange(1, 3);
        }
    }

    [Fact]
    public void Generate_object_includes_all_required_keys()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["a"] = new OpenApiSchema { Type = JsonSchemaType.String },
            },
            Required = new HashSet<string> { "a", "missing" },
        };

        JsonObject obj = sut.Generate(schema).AsObject();
        obj.ContainsKey("a").Should().BeTrue();
        obj.ContainsKey("missing").Should().BeTrue();
        obj["missing"]!.Should().BeOfType<JsonObject>().Which.Count.Should().Be(0);
    }

    [Fact]
    public void Generate_enum_picks_list_member()
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = [JsonValue.Create("alpha"), JsonValue.Create("beta")],
        };

        var set = new HashSet<string>();
        for (int i = 0; i < 40; i++)
        {
            set.Add(sut.Generate(schema).AsValue().GetValue<string>()!);
        }

        set.Should().NotHaveCount(1, "random enum should vary over many draws");
        set.Should().BeSubsetOf(new[] { "alpha", "beta" });
    }

    [Fact]
    public void Generate_allOf_merges_properties_and_required()
    {
        var schema = new OpenApiSchema
        {
            AllOf =
            [
                new OpenApiSchema
                {
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["x"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                    },
                },
                new OpenApiSchema
                {
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["y"] = new OpenApiSchema { Type = JsonSchemaType.String },
                    },
                    Required = new HashSet<string> { "z" },
                },
            ],
        };

        JsonObject obj = sut.Generate(schema).AsObject();
        obj.ContainsKey("x").Should().BeTrue();
        obj.ContainsKey("y").Should().BeTrue();
        obj.ContainsKey("z").Should().BeTrue();
    }

    [Fact]
    public void Generate_oneOf_uses_first_branch()
    {
        var schema = new OpenApiSchema
        {
            OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.String },
                new OpenApiSchema { Type = JsonSchemaType.Integer },
            ],
        };

        sut.Generate(schema).AsValue().GetValueKind().Should().Be(JsonValueKind.String);
    }

    [Fact]
    public void Generate_anyOf_uses_first_branch()
    {
        var schema = new OpenApiSchema
        {
            AnyOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Boolean },
                new OpenApiSchema { Type = JsonSchemaType.String },
            ],
        };

        sut.Generate(schema).AsValue().GetValueKind().Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
    }

    [Fact]
    public void Generate_unknown_type_returns_empty_object()
    {
        var schema = new OpenApiSchema();
        sut.Generate(schema).Should().BeOfType<JsonObject>().Which.Count.Should().Be(0);
    }

    [Fact]
    public async Task Generate_unresolved_file_ref_returns_empty_object()
    {
        var loader = new OpenApiLoader();
        OpenApiDocument doc = await loader.LoadAsync(ResourcePath("external-main.json"));
        IReadOnlyList<RouteDefinition> routes = new MockServerConfigurator().Build(doc);
        IOpenApiSchema? schema = routes[0].Responses[200];
        sut.Generate(schema).Should().BeOfType<JsonObject>().Which.Count.Should().Be(0);
    }

    private static string ResourcePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", fileName);

    [Fact]
    public void Generate_depth_guard_returns_empty_string_for_deep_nesting()
    {
        static OpenApiSchema BuildNested(int remaining)
        {
            if (remaining <= 0)
            {
                return new OpenApiSchema { Type = JsonSchemaType.String };
            }

            return new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema>
                {
                    ["n"] = BuildNested(remaining - 1),
                },
            };
        }

        // Enough nesting to exceed MaxDepth (8) inside GenerateCore.
        OpenApiSchema deep = BuildNested(12);
        JsonNode current = sut.Generate(deep);
        while (current is JsonObject o && o.TryGetPropertyValue("n", out JsonNode? next))
        {
            current = next!;
        }

        current.Should().BeAssignableTo<JsonValue>();
        current.AsValue().GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void Generate_null_json_type_keyword_yields_object_when_properties_exist()
    {
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["k"] = new OpenApiSchema { Type = JsonSchemaType.String },
            },
        };

        JsonObject obj = sut.Generate(schema).AsObject();
        obj.ContainsKey("k").Should().BeTrue();
    }
}
