using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Mockjito.Core.Configuration;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

namespace Mockjito.Core.Middleware;

/// <summary>
/// Intercepts requests, matches OpenAPI routes, and returns generated JSON.
/// </summary>
public sealed class MockRequestMiddleware
{
#pragma warning disable IDE0052, S4487 // RequestDelegate is required for UseMiddleware<T>
    private readonly RequestDelegate next;
#pragma warning restore IDE0052, S4487

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IReadOnlyList<RouteDefinition> routes;
    private readonly RouteMatcher routeMatcher;
    private readonly FakeResponseGenerator fakeResponseGenerator;
    private readonly ILogger<MockRequestMiddleware> logger;
    private readonly MockServerOptions options;

    public MockRequestMiddleware(
        RequestDelegate next,
        IReadOnlyList<RouteDefinition> routes,
        RouteMatcher routeMatcher,
        FakeResponseGenerator fakeResponseGenerator,
        ILogger<MockRequestMiddleware> logger,
        IOptions<MockServerOptions> optionsAccessor)
    {
        this.next = next;
        this.routes = routes;
        this.routeMatcher = routeMatcher;
        this.fakeResponseGenerator = fakeResponseGenerator;
        this.logger = logger;
        options = optionsAccessor.Value;
    }

    /// <summary>
    /// Handles an incoming HTTP request (terminal mock response in the pipeline).
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var method = new HttpMethod(context.Request.Method);
        var match = routeMatcher.Match(routes, method, context.Request.Path);

        if (!match.Success)
        {
            if (match.MethodNotAllowed)
            {
                await WriteMethodNotAllowedAsync(context, sw, match.AllowedMethods).ConfigureAwait(false);
                return;
            }

            await WriteNotFoundAsync(context, sw).ConfigureAwait(false);
            return;
        }

        await WriteMockResponseAsync(context, sw, match.Route!).ConfigureAwait(false);
    }

    private async Task WriteMockResponseAsync(HttpContext context, Stopwatch sw, RouteDefinition route)
    {
        var (statusCode, schema) = SelectResponse(route);
        JsonNode bodyNode;
        try
        {
            bodyNode = fakeResponseGenerator.Generate(schema);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate response for {Method} {Path}", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            var err = new JsonObject
            {
                ["error"] = "Failed to generate fake response",
                ["details"] = ex.Message,
            };
            await context.Response.WriteAsync(err.ToJsonString(JsonWriteOptions)).ConfigureAwait(false);
            sw.Stop();
            LogRequestLine(context, sw);
            return;
        }

        var jsonText = bodyNode.ToJsonString(JsonWriteOptions);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await LogVerboseRequestAsync(context).ConfigureAwait(false);
        await context.Response.WriteAsync(jsonText).ConfigureAwait(false);
        sw.Stop();
        LogRequestLine(context, sw);

        if (options.Verbose)
        {
            logger.LogDebug("Response body:{NewLine}{Body}", Environment.NewLine, jsonText);
        }
    }

    private static (int StatusCode, IOpenApiSchema? Schema) SelectResponse(RouteDefinition route)
    {
        var dict = route.Responses;
        if (dict.Count == 0)
        {
            return (StatusCodes.Status200OK, null);
        }

        var successCodes = dict.Keys.Where(static c => c is >= 200 and <= 299).OrderBy(static c => c).ToList();
        if (successCodes.Count > 0)
        {
            var code = successCodes[0];
            return (code, dict[code]);
        }

        var anyCode = dict.Keys.OrderBy(static c => c).First();
        return (anyCode, dict[anyCode]);
    }

    private async Task WriteNotFoundAsync(HttpContext context, Stopwatch sw)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "application/json";

        var arr = new JsonArray();
        if (options.Verbose)
        {
            foreach (var line in routes.Select(static r => $"{r.Method.Method} {r.PathTemplate}").Distinct().OrderBy(static s => s))
            {
                arr.Add(JsonValue.Create(line));
            }
        }

        var body = new JsonObject
        {
            ["error"] = "Endpoint not found in mock specification",
            ["availableEndpoints"] = arr,
        };

        await LogVerboseRequestAsync(context).ConfigureAwait(false);
        await context.Response.WriteAsync(body.ToJsonString(JsonWriteOptions)).ConfigureAwait(false);
        sw.Stop();
        LogRequestLine(context, sw);
    }

    private async Task WriteMethodNotAllowedAsync(HttpContext context, Stopwatch sw, IReadOnlyList<HttpMethod> allowed)
    {
        context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
        context.Response.ContentType = "application/json";

        var arr = new JsonArray();
        foreach (var m in allowed)
        {
            arr.Add(JsonValue.Create(m.Method));
        }

        var body = new JsonObject
        {
            ["error"] = "Method not allowed for this path in mock specification",
            ["allowedMethods"] = arr,
        };

        await LogVerboseRequestAsync(context).ConfigureAwait(false);
        await context.Response.WriteAsync(body.ToJsonString(JsonWriteOptions)).ConfigureAwait(false);
        sw.Stop();
        LogRequestLine(context, sw);
    }

    private void LogRequestLine(HttpContext context, Stopwatch sw)
    {
        var phrase = GetReasonPhrase(context.Response.StatusCode);
        logger.LogInformation(
            "{Method} {Path} -> {Status} {ReasonPhrase} ({Elapsed}ms)",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            phrase,
            sw.ElapsedMilliseconds);
    }

    private static string GetReasonPhrase(int statusCode) =>
        statusCode switch
        {
            200 => "OK",
            201 => "Created",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => string.Empty,
        };

    private async Task LogVerboseRequestAsync(HttpContext context)
    {
        if (!options.Verbose)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Request headers:");
        foreach (var header in context.Request.Headers)
        {
            sb.Append("  ").Append(header.Key).Append(": ").AppendLine(header.Value.ToString());
        }

        context.Request.EnableBuffering();
        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var bodyText = await reader.ReadToEndAsync().ConfigureAwait(false);
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            sb.AppendLine("Request body:");
            sb.AppendLine(bodyText);
        }

        logger.LogDebug("{VerboseBlock}", sb.ToString());
    }
}
