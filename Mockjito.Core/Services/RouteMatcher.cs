using System.Net.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Mockjito.Core.Models;

namespace Mockjito.Core.Services;

/// <summary>
/// Result of matching an incoming request to a route from the specification.
/// </summary>
public sealed class RouteMatchResult
{
    public bool Success { get; init; }

    public RouteDefinition? Route { get; init; }

    public RouteValueDictionary? RouteValues { get; init; }

    /// <summary>
    /// Path matches a template, but the HTTP method is not described in the specification.
    /// </summary>
    public bool MethodNotAllowed { get; init; }

    public IReadOnlyList<HttpMethod> AllowedMethods { get; init; } = Array.Empty<HttpMethod>();
}

/// <summary>
/// URL matching against OpenAPI templates via <see cref="TemplateMatcher"/>.
/// </summary>
public sealed class RouteMatcher
{
    /// <summary>
    /// Attempts to find an operation by method and path.
    /// </summary>
    public RouteMatchResult Match(IReadOnlyList<RouteDefinition> routes, HttpMethod method, PathString path)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var pathValue = path.HasValue ? path.Value! : "/";
        var normalized = pathValue.StartsWith("/", StringComparison.Ordinal)
            ? pathValue
            : "/" + pathValue;

        var pathMatches = new List<RouteDefinition>();
        foreach (var route in routes)
        {
            var matcher = new TemplateMatcher(route.ParsedTemplate, new RouteValueDictionary());
            var values = new RouteValueDictionary();
            if (matcher.TryMatch(new PathString(normalized), values))
            {
                pathMatches.Add(route);
            }
        }

        if (pathMatches.Count == 0)
        {
            return new RouteMatchResult();
        }

        var sameMethod = pathMatches.FirstOrDefault(r => r.Method == method);
        if (sameMethod is not null)
        {
            var matcher = new TemplateMatcher(sameMethod.ParsedTemplate, new RouteValueDictionary());
            var values = new RouteValueDictionary();
            _ = matcher.TryMatch(new PathString(normalized), values);
            return new RouteMatchResult
            {
                Success = true,
                Route = sameMethod,
                RouteValues = values,
            };
        }

        return new RouteMatchResult
        {
            MethodNotAllowed = true,
            AllowedMethods = pathMatches.Select(static r => r.Method).Distinct().ToList(),
        };
    }
}
