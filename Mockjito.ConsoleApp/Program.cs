using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mockjito.Core.Configuration;
using Mockjito.Core.Middleware;
using Mockjito.Core.Models;
using Mockjito.Core.Services;

var root = BuildRootCommand();
var parseResult = root.Parse(args, new ParserConfiguration());
return await parseResult.InvokeAsync(new InvocationConfiguration()).ConfigureAwait(false);

static RootCommand BuildRootCommand()
{
    var root = new RootCommand("Mockjito — OpenAPI (Swagger) mock server.");

    root.SetAction(_ =>
    {
        PrintBanner();
        Console.Out.WriteLine();
        Console.Out.WriteLine("Usage: mockjito serve --file <path> [--port <port>] [--verbose]");
        Console.Out.WriteLine("Serve command help: mockjito serve --help");
        return 0;
    });

    var serve = new Command("serve", "Start an in-memory mock server (Kestrel) from an OpenAPI file.");

    var fileOption = new Option<FileInfo>("--file", "-f")
    {
        Description = "Path to the specification file (.json, .yaml, .yml).",
        Required = true,
    };

    var portOption = new Option<int>("--port", "-p")
    {
        Description = "HTTP server port.",
        DefaultValueFactory = _ => 5000,
    };

    var verboseOption = new Option<bool>("--verbose", "-v")
    {
        Description = "Verbose logging (request/response headers and bodies).",
        DefaultValueFactory = _ => false,
    };

    serve.Add(fileOption);
    serve.Add(portOption);
    serve.Add(verboseOption);

    serve.SetAction(async parseResult =>
    {
        if (parseResult.Errors.Count > 0)
        {
            foreach (var err in parseResult.Errors)
            {
                await Console.Error.WriteLineAsync(err.Message).ConfigureAwait(false);
            }

            await Console.Error.WriteLineAsync().ConfigureAwait(false);
            await Console.Error.WriteLineAsync("Help: mockjito serve --help").ConfigureAwait(false);
            return 1;
        }

        var file = parseResult.GetValue(fileOption);
        if (file is null || !file.Exists)
        {
            await Console.Error.WriteLineAsync("Error: provide an existing specification file (--file / -f).")
                .ConfigureAwait(false);
            await Console.Error.WriteLineAsync("Help: mockjito serve --help").ConfigureAwait(false);
            return 1;
        }

        var port = parseResult.GetValue(portOption);
        var verbose = parseResult.GetValue(verboseOption);

        PrintBanner();

        var loader = new OpenApiLoader();
        var document = await loader.LoadAsync(file.FullName).ConfigureAwait(false);

        var configurator = new MockServerConfigurator();
        var routes = configurator.Build(document);

        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.WebHost.UseKestrel();

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
        });
        builder.Logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);

        builder.Services.AddSingleton<RouteMatcher>();
        builder.Services.AddSingleton<FakeResponseGenerator>();
        builder.Services.AddSingleton<IReadOnlyList<RouteDefinition>>(routes);
        builder.Services.AddCors();
        builder.Services.Configure<MockServerOptions>(o =>
        {
            o.Port = port;
            o.Verbose = verbose;
        });

        var app = builder.Build();

        app.UseCors(policy => policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader());

        app.UseMiddleware<MockRequestMiddleware>();

        await Console.Out.WriteLineAsync().ConfigureAwait(false);
        await Console.Out.WriteLineAsync(
                "🍹 Mockjito 1.0.0 – Mock it refreshingly. No hangover from failing production servers.")
            .ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Mock server listening on http://localhost:{port}").ConfigureAwait(false);
        await Console.Out.WriteLineAsync($"Loaded {routes.Count} endpoints.").ConfigureAwait(false);
        await Console.Out.WriteLineAsync().ConfigureAwait(false);

        await app.RunAsync().ConfigureAwait(false);
        return 0;
    });

    root.Add(serve);
    return root;
}

static void PrintBanner()
{
    Console.Out.WriteLine("Mockjito — Mock it refreshingly. No hangover from failing production servers.");
}
