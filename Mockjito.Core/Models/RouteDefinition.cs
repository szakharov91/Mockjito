using System.Net.Http;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.OpenApi;

namespace Mockjito.Core.Models;

/// <summary>
/// OpenAPI operation parameter (path, query, header).
/// </summary>
public sealed record OpenApiParameterInfo(
    string Name,
    ParameterLocation In,
    bool Required,
    IOpenApiSchema? Schema);

/// <summary>
/// Definition of a single HTTP route from the specification.
/// </summary>
public sealed record RouteDefinition(
    HttpMethod Method,
    string PathTemplate,
    RouteTemplate ParsedTemplate,
    IReadOnlyList<OpenApiParameterInfo> Parameters,
    IReadOnlyDictionary<int, IOpenApiSchema?> Responses);
