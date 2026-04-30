using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Mockjito.Core.Configuration;
using Mockjito.Core.Middleware;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

namespace Mockjito.Core.Tests;

public sealed class MockRequestMiddlewareTests
{
    private static string ResourcePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Resources", fileName);

    private static IWebHostBuilder CreateHost(
        IReadOnlyList<RouteDefinition> routes,
        FakeResponseGenerator generator,
        bool verbose)
    {
        return new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(routes);
                services.AddSingleton<RouteMatcher>();
                services.AddSingleton(generator);
                services.AddSingleton<ILogger<MockRequestMiddleware>>(_ =>
                    NullLogger<MockRequestMiddleware>.Instance);
                services.Configure<MockServerOptions>(o => o.Verbose = verbose);
            })
            .Configure(app => app.UseMiddleware<MockRequestMiddleware>());
    }

    [Fact]
    public async Task Invoke_returns_200_json_for_matched_route()
    {
        var schema = new OpenApiSchema { Type = JsonSchemaType.Object };
        var routes = new List<RouteDefinition>
        {
            new(
                HttpMethod.Get,
                "/ping",
                TemplateParser.Parse("ping"),
                Array.Empty<OpenApiParameterInfo>(),
                new Dictionary<int, IOpenApiSchema?> { [200] = schema }),
        };

        using var server = new TestServer(CreateHost(routes, new FakeResponseGenerator(), verbose: false));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        string json = await response.Content.ReadAsStringAsync();
        JsonNode.Parse(json).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public async Task Invoke_returns_404_with_available_endpoints_when_verbose()
    {
        var routes = new List<RouteDefinition>
        {
            new(
                HttpMethod.Get,
                "/a",
                TemplateParser.Parse("a"),
                Array.Empty<OpenApiParameterInfo>(),
                new Dictionary<int, IOpenApiSchema?>()),
        };

        using var server = new TestServer(CreateHost(routes, new FakeResponseGenerator(), verbose: true));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/missing");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        JsonObject root = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        root["availableEndpoints"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task Invoke_returns_405_with_allowed_methods()
    {
        var routes = new List<RouteDefinition>
        {
            new(
                HttpMethod.Get,
                "/x",
                TemplateParser.Parse("x"),
                Array.Empty<OpenApiParameterInfo>(),
                new Dictionary<int, IOpenApiSchema?>()),
        };

        using var server = new TestServer(CreateHost(routes, new FakeResponseGenerator(), verbose: false));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.PostAsync("/x", null);
        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        JsonObject root = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        var arr = root["allowedMethods"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        arr.Should().Contain("GET");
    }

    [Fact]
    public async Task Invoke_returns_500_when_generator_throws()
    {
        var routes = new List<RouteDefinition>
        {
            new(
                HttpMethod.Get,
                "/boom",
                TemplateParser.Parse("boom"),
                Array.Empty<OpenApiParameterInfo>(),
                new Dictionary<int, IOpenApiSchema?> { [200] = new OpenApiSchema { Type = JsonSchemaType.String } }),
        };

        using var server = new TestServer(CreateHost(routes, new ThrowingFakeResponseGenerator(), verbose: false));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/boom");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        JsonObject root = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        root["error"]!.GetValue<string>().Should().Contain("generate");
        root["details"]!.GetValue<string>().Should().Contain("boom");
    }

    [Fact]
    public async Task Integration_loaded_spec_get_returns_json()
    {
        var loader = new OpenApiLoader();
        OpenApiDocument doc = await loader.LoadAsync(ResourcePath("minimal.json"));
        IReadOnlyList<RouteDefinition> routes = new MockServerConfigurator().Build(doc);

        using var server = new TestServer(CreateHost(routes, new FakeResponseGenerator(), verbose: false));
        using HttpClient client = server.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    private sealed class ThrowingFakeResponseGenerator : FakeResponseGenerator
    {
        public override JsonNode Generate(IOpenApiSchema? schema, string? propertyHint = null) =>
            throw new InvalidOperationException("boom from fake generator");
    }
}
