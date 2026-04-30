using System.Net.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.OpenApi;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

namespace Mockjito.Core.Tests;

public sealed class RouteMatcherTests
{
    private readonly RouteMatcher sut = new();

    private static RouteDefinition Route(HttpMethod method, string pathTemplate) =>
        new(
            method,
            pathTemplate,
            TemplateParser.Parse(pathTemplate.TrimStart('/')),
            Array.Empty<OpenApiParameterInfo>(),
            new Dictionary<int, IOpenApiSchema?>());

    [Fact]
    public void Match_exact_path_and_method_succeeds()
    {
        var routes = new List<RouteDefinition> { Route(HttpMethod.Get, "/hello") };
        RouteMatchResult result = sut.Match(routes, HttpMethod.Get, new PathString("/hello"));
        result.Success.Should().BeTrue();
        result.Route.Should().NotBeNull();
        result.RouteValues.Should().NotBeNull();
    }

    [Fact]
    public void Match_parameterized_path_extracts_route_values()
    {
        var routes = new List<RouteDefinition> { Route(HttpMethod.Get, "/items/{id}") };
        RouteMatchResult result = sut.Match(routes, HttpMethod.Get, new PathString("/items/42"));
        result.Success.Should().BeTrue();
        result.RouteValues!["id"].Should().Be("42");
    }

    [Fact]
    public void Match_unknown_path_returns_failure()
    {
        var routes = new List<RouteDefinition> { Route(HttpMethod.Get, "/known") };
        RouteMatchResult result = sut.Match(routes, HttpMethod.Get, new PathString("/missing"));
        result.Success.Should().BeFalse();
        result.MethodNotAllowed.Should().BeFalse();
    }

    [Fact]
    public void Match_wrong_method_returns_method_not_allowed_with_allowed_list()
    {
        var routes = new List<RouteDefinition>
        {
            Route(HttpMethod.Get, "/resource"),
            Route(HttpMethod.Post, "/resource"),
        };

        RouteMatchResult result = sut.Match(routes, HttpMethod.Delete, new PathString("/resource"));
        result.Success.Should().BeFalse();
        result.MethodNotAllowed.Should().BeTrue();
        result.AllowedMethods.Should().BeEquivalentTo(new[] { HttpMethod.Get, HttpMethod.Post });
    }
}
