using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameDB.Infrastructure.Converters; // замініть на свій namespace

public class NumberToStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Якщо JSON-значення — це число (наприклад, 12345), конвертуємо його в рядок
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt64().ToString();
        }
        
        // Якщо це і так рядок — просто повертаємо
        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        // Обробляємо випадок, якщо значення null
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        throw new JsonException($"Неможливо сконвертувати {reader.TokenType} у string.");
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}