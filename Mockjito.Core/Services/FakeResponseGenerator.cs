using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bogus;
using Microsoft.OpenApi;

namespace Mockjito.Core.Services;

/// <summary>
/// Generates a JSON response from an OpenAPI schema using Bogus.
/// </summary>
public class FakeResponseGenerator
{
    private const int MaxDepth = 8;
    private const int DefaultMinNumber = 0;
    private const int DefaultMaxNumber = 1000;
    private readonly Faker faker = new("ru");
    private readonly object fakerLock = new();

    /// <summary>
    /// Builds a JSON node for the response schema.
    /// </summary>
    public virtual JsonNode Generate(IOpenApiSchema? schema, string? propertyHint = null)
    {
        if (schema is null)
        {
            return new JsonObject();
        }

        try
        {
            return GenerateCore(schema, propertyHint, 0);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to generate fake data from the schema.", ex);
        }
    }

    private JsonNode GenerateCore(IOpenApiSchema schema, string? propertyHint, int depth)
    {
        if (depth > MaxDepth)
        {
            return JsonValue.Create(string.Empty)!;
        }

        if (schema is IOpenApiReferenceHolder holder && holder.UnresolvedReference)
        {
            return new JsonObject();
        }

        if (schema.Enum is { Count: > 0 } enumList)
        {
            return PickRandomEnumValue(enumList);
        }

        if (schema.OneOf is { Count: > 0 })
        {
            return GenerateCore(schema.OneOf[0], propertyHint, depth + 1);
        }

        if (schema.AnyOf is { Count: > 0 })
        {
            return GenerateCore(schema.AnyOf[0], propertyHint, depth + 1);
        }

        if (schema.AllOf is { Count: > 0 })
        {
            return MergeAllOf(schema.AllOf, depth);
        }

        var typeKeyword = GetTypeKeyword(schema);
        if (string.IsNullOrEmpty(typeKeyword))
        {
            if (schema.Properties is { Count: > 0 })
            {
                typeKeyword = "object";
            }
            else if (schema.Items is not null)
            {
                typeKeyword = "array";
            }
        }

        return typeKeyword switch
        {
            "string" => JsonValue.Create(GenerateString(schema, propertyHint))!,
            "integer" => JsonValue.Create(GenerateInteger(schema))!,
            "number" => JsonValue.Create(GenerateNumber(schema))!,
            "boolean" => JsonValue.Create(GenerateBool())!,
            "array" => GenerateArray(schema, depth),
            "object" => GenerateObject(schema, depth),
            "null" => JsonValue.Create((string?)null)!,
            _ => new JsonObject(),
        };
    }

    private static string? GetTypeKeyword(IOpenApiSchema schema)
    {
        var jsonType = schema.Type;
        if (jsonType is null || jsonType == default)
        {
            return null;
        }

        return MapJsonSchemaType(jsonType.Value);
    }

    private static string? MapJsonSchemaType(JsonSchemaType t)
    {
        if (t == default)
        {
            return null;
        }

        if (t.HasFlag(JsonSchemaType.Array))
        {
            return "array";
        }

        if (t.HasFlag(JsonSchemaType.Object))
        {
            return "object";
        }

        if (t.HasFlag(JsonSchemaType.String))
        {
            return "string";
        }

        if (t.HasFlag(JsonSchemaType.Number))
        {
            return "number";
        }

        if (t.HasFlag(JsonSchemaType.Integer))
        {
            return "integer";
        }

        if (t.HasFlag(JsonSchemaType.Boolean))
        {
            return "boolean";
        }

        if (t.HasFlag(JsonSchemaType.Null))
        {
            return "null";
        }

        return null;
    }

    private JsonNode MergeAllOf(IList<IOpenApiSchema> allOf, int depth)
    {
        var merged = new JsonObject();
        var required = new HashSet<string>(StringComparer.Ordinal);

        foreach (var part in allOf)
        {
            if (part.Required is not null)
            {
                foreach (var r in part.Required)
                {
                    required.Add(r);
                }
            }

            if (part.Properties is null || part.Properties.Count == 0)
            {
                continue;
            }

            foreach (var prop in part.Properties)
            {
                var value = GenerateCore(prop.Value, prop.Key, depth + 1);
                merged[prop.Key] = value;
            }
        }

        foreach (var name in required)
        {
            if (!merged.ContainsKey(name))
            {
                merged[name] = new JsonObject();
            }
        }

        return merged;
    }

    private JsonArray GenerateArray(IOpenApiSchema schema, int depth)
    {
        var array = new JsonArray();
        var items = schema.Items;
        if (items is null)
        {
            return array;
        }

        int count;
        lock (fakerLock)
        {
            count = faker.Random.Int(1, 3);
        }

        for (var i = 0; i < count; i++)
        {
            array.Add(GenerateCore(items, null, depth + 1));
        }

        return array;
    }

    private JsonObject GenerateObject(IOpenApiSchema schema, int depth)
    {
        var obj = new JsonObject();
        if (schema.Properties is null || schema.Properties.Count == 0)
        {
            return obj;
        }

        foreach (var prop in schema.Properties)
        {
            obj[prop.Key] = GenerateCore(prop.Value, prop.Key, depth + 1);
        }

        if (schema.Required is not null)
        {
            foreach (var req in schema.Required)
            {
                if (!obj.ContainsKey(req))
                {
                    obj[req] = new JsonObject();
                }
            }
        }

        return obj;
    }

    private JsonNode PickRandomEnumValue(IList<JsonNode> enumValues)
    {
        var nonNull = enumValues.Where(static n => n is not null).ToList();
        if (nonNull.Count == 0)
        {
            return new JsonObject();
        }

        JsonNode chosen;
        lock (fakerLock)
        {
            chosen = nonNull[faker.Random.Int(0, nonNull.Count - 1)]!;
        }

        return JsonNode.Parse(chosen.ToJsonString())!;
    }

    private string GenerateString(IOpenApiSchema schema, string? propertyHint)
    {
        var format = schema.Format?.ToString().ToLowerInvariant();
        lock (fakerLock)
        {
            return format switch
            {
                "date" => faker.Date.Past().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "date-time" => faker.Date.Recent().ToString("o", CultureInfo.InvariantCulture),
                "email" => faker.Internet.Email(),
                "uri" or "url" => faker.Internet.Url(),
                "uuid" => Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture),
                _ => GenerateStringByPropertyHint(propertyHint),
            };
        }
    }

    private string GenerateStringByPropertyHint(string? propertyHint)
    {
        lock (fakerLock)
        {
            if (string.IsNullOrEmpty(propertyHint))
            {
                return faker.Lorem.Word();
            }

            return propertyHint.ToLowerInvariant() switch
            {
                "name" or "fullname" => faker.Name.FullName(),
                "firstname" => faker.Name.FirstName(),
                "lastname" => faker.Name.LastName(),
                "city" => faker.Address.City(),
                "country" => faker.Address.Country(),
                "street" => faker.Address.StreetAddress(),
                "phone" or "phonenumber" => faker.Phone.PhoneNumber(),
                _ => faker.Lorem.Word(),
            };
        }
    }

    private int GenerateInteger(IOpenApiSchema schema)
    {
        var (min, max) = GetNumberBounds(schema);
        lock (fakerLock)
        {
            return faker.Random.Int((int)Math.Round(min), (int)Math.Round(max));
        }
    }

    private double GenerateNumber(IOpenApiSchema schema)
    {
        var (min, max) = GetNumberBounds(schema);
        lock (fakerLock)
        {
            return faker.Random.Double(min, max);
        }
    }

    private bool GenerateBool()
    {
        lock (fakerLock)
        {
            return faker.Random.Bool();
        }
    }

    private (double Min, double Max) GetNumberBounds(IOpenApiSchema schema)
    {
        double min = DefaultMinNumber;
        double max = DefaultMaxNumber;

        if (TryParseSchemaNumber(schema.Minimum, out var minParsed))
        {
            min = minParsed;
        }

        if (TryParseSchemaNumber(schema.Maximum, out var maxParsed))
        {
            max = maxParsed;
        }

        if (min > max)
        {
            (min, max) = (max, min);
        }

        if (Math.Abs(max - min) < double.Epsilon)
        {
            max = min + 1d;
        }

        return (min, max);
    }

    private static bool TryParseSchemaNumber(string? value, out double result)
    {
        result = 0;
        return !string.IsNullOrWhiteSpace(value)
               && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }
}
