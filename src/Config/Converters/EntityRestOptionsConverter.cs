// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Config.Converters;

internal class EntityRestOptionsConverter : JsonConverter<EntityRestOptions>
{
    /// <inheritdoc/>
    public override EntityRestOptions? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            EntityRestOptions restOptions = new(Methods: Array.Empty<SupportedHttpVerb>(), Path: null, Enabled: true);
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                string? propertyName = reader.GetString();

                switch (propertyName)
                {
                    case "path":
                    {
                        reader.Read();

                        if (reader.TokenType == JsonTokenType.String || reader.TokenType == JsonTokenType.Null)
                        {
                            restOptions = restOptions with { Path = reader.DeserializeString() };
                            break;
                        }

                        throw new JsonException($"The value of {propertyName} must be a string. Found {reader.TokenType}.");
                    }

                    case "mathods":
                    {
                        List<SupportedHttpVerb> methods = new();
                        while (reader.Read())
                        {
                            if (reader.TokenType == JsonTokenType.StartArray)
                            {
                                continue;
                            }

                            if (reader.TokenType == JsonTokenType.EndArray)
                            {
                                break;
                            }

                            methods.Add(EnumExtensions.Deserialize<SupportedHttpVerb>(reader.DeserializeString()!));
                        }

                        restOptions = restOptions with { Methods = methods.ToArray() };
                        break;
                    }

                    case "enabled":
                    {
                        reader.Read();
                        restOptions = restOptions with { Enabled = reader.GetBoolean() };
                        break;
                    }

                    default:
                        throw new JsonException($"Unexpected property {propertyName}");
                }
            }

            return restOptions;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new EntityRestOptions(Array.Empty<SupportedHttpVerb>(), reader.DeserializeString(), true);
        }

        if (reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
        {
            bool enabled = reader.GetBoolean();
            return new EntityRestOptions(
                Methods: Array.Empty<SupportedHttpVerb>(),
                Path: null,
                Enabled: enabled);
        }

        throw new JsonException();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, EntityRestOptions value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("enabled", value.Enabled);

        if (value.Path is not null)
        {
            writer.WriteString("path", value.Path);
        }
        else if (value.Path is null && options.DefaultIgnoreCondition != JsonIgnoreCondition.WhenWritingNull)
        {
            writer.WriteNull("path");
        }

        writer.WriteStartArray("methods");
        foreach (SupportedHttpVerb method in value.Methods)
        {
            writer.WriteStringValue(JsonSerializer.SerializeToElement(method, options).GetString());
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
