using System.Net.Http;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.OpenApi;
using Mockjito.Core.Models;

namespace Mockjito.Core.Services;

/// <summary>
/// Builds a list of routes and response schemas from an OpenAPI document.
/// </summary>
public sealed class MockServerConfigurator
{
    /// <summary>
    /// Collects routes for all operations in paths.
    /// </summary>
    public IReadOnlyList<RouteDefinition> Build(OpenApiDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var routes = new List<RouteDefinition>();

        foreach (var pathPair in doc.Paths)
        {
            var pathKey = pathPair.Key;
            if (pathPair.Value is not OpenApiPathItem pathItem)
            {
                continue;
            }

            var operations = pathItem.Operations;
            if (operations is null || operations.Count == 0)
            {
                continue;
            }

            foreach (var opPair in operations)
            {
                var method = opPair.Key;
                var operation = opPair.Value;
                if (operation is null)
                {
                    continue;
                }

                var parameters = CollectParameters(pathItem, operation);
                var responses = CollectResponses(operation);

                var templateText = pathKey.TrimStart('/');
                RouteTemplate parsed;
                try
                {
                    parsed = TemplateParser.Parse(string.IsNullOrEmpty(templateText) ? "/" : templateText);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to parse path template '{pathKey}' for {method.Method} {pathKey}.", ex);
                }

                routes.Add(new RouteDefinition(
                    method,
                    pathKey,
                    parsed,
                    parameters,
                    responses));
            }
        }

        return routes;
    }

    private static IReadOnlyList<OpenApiParameterInfo> CollectParameters(
        OpenApiPathItem pathItem,
        OpenApiOperation operation)
    {
        var list = new List<OpenApiParameterInfo>();

        void AddParams(IEnumerable<IOpenApiParameter>? parameters)
        {
            if (parameters is null)
            {
                return;
            }

            foreach (var p in parameters)
            {
                if (p is null || string.IsNullOrEmpty(p.Name))
                {
                    continue;
                }

                var location = p.In ?? ParameterLocation.Query;
                list.Add(new OpenApiParameterInfo(
                    p.Name,
                    location,
                    p.Required,
                    p.Schema));
            }
        }

        AddParams(pathItem.Parameters);
        AddParams(operation.Parameters);

        return list;
    }

    private static IReadOnlyDictionary<int, IOpenApiSchema?> CollectResponses(OpenApiOperation operation)
    {
        var map = new Dictionary<int, IOpenApiSchema?>();
        var responses = operation.Responses;
        if (responses is null || responses.Count == 0)
        {
            return map;
        }

        IOpenApiSchema? defaultSchema = null;

        foreach (var pair in responses)
        {
            if (pair.Value is not OpenApiResponse response)
            {
                continue;
            }

            var schema = ExtractResponseSchema(response);

            if (string.Equals(pair.Key, "default", StringComparison.OrdinalIgnoreCase))
            {
                defaultSchema = schema;
                continue;
            }

            if (!int.TryParse(pair.Key, out var code))
            {
                continue;
            }

            map[code] = schema;
        }

        var has2xx = map.Keys.Any(static c => c is >= 200 and <= 299);
        if (defaultSchema is not null && !has2xx && !map.ContainsKey(200))
        {
            map[200] = defaultSchema;
        }

        return map;
    }

    private static IOpenApiSchema? ExtractResponseSchema(OpenApiResponse response)
    {
        var content = response.Content;
        if (content is null || content.Count == 0)
        {
            return null;
        }

        if (content.TryGetValue("application/json", out var jsonMedia) && jsonMedia?.Schema is not null)
        {
            return jsonMedia.Schema;
        }

        foreach (var media in content.Values)
        {
            if (media?.Schema is not null)
            {
                return media.Schema;
            }
        }

        return null;
    }
}
