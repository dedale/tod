namespace Tod;

using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class SingleStringValueConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        var prop = typeToConvert.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
        return prop != null && prop.PropertyType == typeof(string);
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var prop = typeToConvert.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance)!
            ?? throw new InvalidOperationException($"Type {typeToConvert} must have a public string Value property.");

        // Prefer single-string ctor if present
        var ctor = typeToConvert.GetConstructor([typeof(string)]);

        // If no single-string ctor, accept parameterless ctor and writable Value property
        ConstructorInfo? parameterlessCtor = null;
        var hasSetter = prop.GetSetMethod() != null;
        if (ctor == null)
        {
            parameterlessCtor = typeToConvert.GetConstructor(Type.EmptyTypes);
            if (parameterlessCtor == null || !hasSetter)
                throw new InvalidOperationException($"Type {typeToConvert} must have either a constructor(string) or a parameterless ctor and writable Value property.");
        }

        var converterType = typeof(SingleStringValueConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType, ctor, parameterlessCtor, prop)!;
    }

    private sealed class SingleStringValueConverter<T> : JsonConverter<T> where T : class
    {
        private readonly ConstructorInfo? _stringCtor;
        private readonly ConstructorInfo? _parameterlessCtor;
        private readonly PropertyInfo _valueProp;

        public SingleStringValueConverter(ConstructorInfo? stringCtor, ConstructorInfo? parameterlessCtor, PropertyInfo valueProp)
        {
            _stringCtor = stringCtor;
            _parameterlessCtor = parameterlessCtor;
            _valueProp = valueProp;
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            //if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType != JsonTokenType.String) throw new JsonException("Expected string token.");

            var s = reader.GetString()!;
            if (_stringCtor != null)
            {
                return (T?)_stringCtor.Invoke([s]);
            }

            var inst = (T?)_parameterlessCtor!.Invoke(null);
            _valueProp.SetValue(inst, s);
            return inst;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            // Don't know if this is needed
            //if (value == null)
            //{
            //    writer.WriteNullValue();
            //    return;
            //}

            var s = (string?)_valueProp.GetValue(value);
            if (s == null)
                writer.WriteNullValue();
            else
                writer.WriteStringValue(s);
        }
    }
}
