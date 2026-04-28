# Mockjito 1.0.0

**Slogan:** *"Mock it refreshingly. No hangover from failing production servers."*

A **.NET 8** console utility that reads a local **OpenAPI 2/3** specification file (JSON or YAML), starts an in-memory **Kestrel** server, and serves generated JSON responses for all described endpoints (powered by **Bogus**). It is designed for local development and testing when the real API is unavailable.

## Build

```bash
dotnet build Mockjito.sln -c Release
```

Output binary: `Mockjito.ConsoleApp/bin/Release/net8.0/mockjito.dll` (or `mockjito.exe` after publishing).

## Run

When using `dotnet run --project Mockjito.ConsoleApp`, the process working directory is `Mockjito.ConsoleApp/`, so relative paths for `--file` are resolved from there (for example, `samples/minimal-openapi.json`).

Show greeting and usage hint (without subcommand):

```bash
dotnet run --project Mockjito.ConsoleApp
```

Show help for the `serve` command:

```bash
dotnet run --project Mockjito.ConsoleApp -- serve --help
```

Start the mock server:

```bash
dotnet run --project Mockjito.ConsoleApp -- serve --file samples/minimal-openapi.json --port 5000
```

Run with verbose logging (request/response headers and bodies):

```bash
dotnet run --project Mockjito.ConsoleApp -- serve --file samples/minimal-openapi.json --verbose
```

After publishing as a global tool (or when running `mockjito` from `PATH`):

```bash
mockjito serve --file spec.yaml --port 5000 --verbose
```

On startup, the console prints:

- Mockjito greeting;
- `Mock server listening on http://localhost:{port}`;
- `Loaded {N} endpoints.`

Quick request check (in another terminal):

```bash
curl http://localhost:5000/hello
```

## `serve` Parameters

| Parameter | Short | Description |
|----------|-----------|----------|
| `--file` | `-f` | Path to `.json`, `.yaml`, or `.yml` (**required**). |
| `--port` | `-p` | Port number (default: `5000`). |
| `--verbose` | `-v` | Extended logging mode. |

## Version 1.0.0.0 Limitations

- Reads specs from **local disk only**.
- External **`$ref`** is not resolved (`LoadExternalRefs = false`).

## Solution Structure

- `Mockjito.ConsoleApp` - entry point, CLI (**System.CommandLine**), Kestrel host.
- `Mockjito.Core` - OpenAPI loading, route extraction, fake response generation, middleware.

## License

See the project repository.
