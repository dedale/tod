using NUnit.Framework;
using System.Text.Json;

namespace Tod.Tests;

[TestFixture]
internal sealed class SingleStringValueConverterFactoryTests
{
    // Test types with string constructor
    private sealed record TypeWithStringCtor(string Value);

    // Test type with parameterless constructor and writable Value property
    private sealed class TypeWithParameterlessCtor
    {
        public string Value { get; set; } = string.Empty;
    }

    // Test type with both constructors (should prefer string constructor)
    private sealed class TypeWithBothCtors
    {
        public string Value { get; set; } = string.Empty;

        public TypeWithBothCtors() { }

        public TypeWithBothCtors(string value)
        {
            Value = value;
        }
    }

    // Test type without Value property
    private sealed class TypeWithoutValueProperty
    {
        public string Name { get; set; } = string.Empty;
    }

    // Test type with non-string Value property
    private sealed class TypeWithNonStringValue
    {
        public int Value { get; set; }
    }

    // Test type with read-only Value property and no string constructor
    private sealed class TypeWithReadOnlyValue
    {
        public string Value { get; } = string.Empty;
    }

    private sealed class TypeWithoutDefaultCtor
    {
        public string Value { get; } = string.Empty;

        public TypeWithoutDefaultCtor(int value)
        {
            Value = value.ToString();
        }
    }

    // Test type with private Value property
    private sealed class TypeWithPrivateValue
    {
        private string Value { get; set; } = string.Empty;
    }

    private SingleStringValueConverterFactory _factory = null!;

    [SetUp]
    public void Setup()
    {
        _factory = new SingleStringValueConverterFactory();
    }

    [Test]
    public void CanConvert_TypeWithStringCtor_ReturnsTrue()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithStringCtor)), Is.True);
    }

    [Test]
    public void CanConvert_TypeWithParameterlessCtor_ReturnsTrue()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithParameterlessCtor)), Is.True);
    }

    [Test]
    public void CanConvert_TypeWithBothCtors_ReturnsTrue()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithBothCtors)), Is.True);
    }

    [Test]
    public void CanConvert_TypeWithoutValueProperty_ReturnsFalse()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithoutValueProperty)), Is.False);
    }

    [Test]
    public void CanConvert_TypeWithNonStringValue_ReturnsFalse()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithNonStringValue)), Is.False);
    }

    [Test]
    public void CanConvert_TypeWithPrivateValue_ReturnsFalse()
    {
        Assert.That(_factory.CanConvert(typeof(TypeWithPrivateValue)), Is.False);
    }

    [Test]
    public void CreateConverter_TypeWithStringCtor_CreatesConverter()
    {
        var converter = _factory.CreateConverter(typeof(TypeWithStringCtor), new JsonSerializerOptions());
        Assert.That(converter, Is.Not.Null);
    }

    [Test]
    public void CreateConverter_TypeWithParameterlessCtor_CreatesConverter()
    {
        var converter = _factory.CreateConverter(typeof(TypeWithParameterlessCtor), new JsonSerializerOptions());
        Assert.That(converter, Is.Not.Null);
    }

    [Test]
    public void CreateConverter_TypeWithoutValueProperty_ThrowsInvalidOperationException()
    {
        Assert.That(
            () => _factory.CreateConverter(typeof(TypeWithoutValueProperty), new JsonSerializerOptions()),
            Throws.InvalidOperationException
                .With.Message.Contains("must have a public string Value property"));
    }

    [Test]
    public void CreateConverter_TypeWithReadOnlyValue_ThrowsInvalidOperationException()
    {
        Assert.That(
            () => _factory.CreateConverter(typeof(TypeWithReadOnlyValue), new JsonSerializerOptions()),
            Throws.InvalidOperationException
                .With.Message.Contains("must have either a constructor(string) or a parameterless ctor and writable Value property"));
    }

    [Test]
    public void CreateConverter_TypeWithoutDefaultCtor_ThrowsInvalidOperationException()
    {
        Assert.That(
            () => _factory.CreateConverter(typeof(TypeWithoutDefaultCtor), new JsonSerializerOptions()),
            Throws.InvalidOperationException
                .With.Message.Contains("must have either a constructor(string) or a parameterless ctor and writable Value property"));
    }

    [Test]
    public void Serialize_TypeWithStringCtor_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithStringCtor("test-value");
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Is.EqualTo("\"test-value\""));
    }

    [Test]
    public void Serialize_TypeWithParameterlessCtor_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithParameterlessCtor { Value = "test-value" };
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Is.EqualTo("\"test-value\""));
    }

    [Test]
    public void Serialize_Null_SerializesAsNull()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        TypeWithStringCtor? obj = null;
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Is.EqualTo("null"));
    }

    [Test]
    public void Serialize_NullString_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithStringCtor(null!);
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Is.EqualTo("null"));
    }

    [Test]
    public void Serialize_EmptyString_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithStringCtor(string.Empty);
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Is.EqualTo("\"\""));
    }

    [Test]
    public void Serialize_StringWithSpecialCharacters_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithStringCtor("test\"with\\special\nchars");
        var json = JsonSerializer.Serialize(obj, options);

        Assert.That(json, Does.Contain("test"));
    }

    [Test]
    public void Deserialize_TypeWithStringCtor_DeserializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "\"test-value\"";
        var obj = JsonSerializer.Deserialize<TypeWithStringCtor>(json, options);

        Assert.That(obj, Is.Not.Null);
        Assert.That(obj!.Value, Is.EqualTo("test-value"));
    }

    [Test]
    public void Deserialize_TypeWithParameterlessCtor_DeserializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "\"test-value\"";
        var obj = JsonSerializer.Deserialize<TypeWithParameterlessCtor>(json, options);

        Assert.That(obj, Is.Not.Null);
        Assert.That(obj!.Value, Is.EqualTo("test-value"));
    }

    [Test]
    public void Deserialize_NullJson_ReturnsNull()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "null";
        var obj = JsonSerializer.Deserialize<TypeWithStringCtor>(json, options);

        Assert.That(obj, Is.Null);
    }

    [Test]
    public void Deserialize_EmptyString_DeserializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "\"\"";
        var obj = JsonSerializer.Deserialize<TypeWithStringCtor>(json, options);

        Assert.That(obj, Is.Not.Null);
        Assert.That(obj!.Value, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Deserialize_NonStringToken_ThrowsJsonException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "123";

        Assert.That(
            () => JsonSerializer.Deserialize<TypeWithStringCtor>(json, options),
            Throws.TypeOf<JsonException>()
                .With.Message.Contains("Expected string token"));
    }

    [Test]
    public void Deserialize_ObjectToken_ThrowsJsonException()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "{\"Value\":\"test\"}";

        Assert.That(
            () => JsonSerializer.Deserialize<TypeWithStringCtor>(json, options),
            Throws.TypeOf<JsonException>()
                .With.Message.Contains("Expected string token"));
    }

    [Test]
    public void SerializeDeserialize_RoundTrip_PreservesValue()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var original = new TypeWithStringCtor("round-trip-test");
        var json = JsonSerializer.Serialize(original, options);
        var deserialized = JsonSerializer.Deserialize<TypeWithStringCtor>(json, options);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Value, Is.EqualTo(original.Value));
    }

    [Test]
    public void SerializeDeserialize_TypeWithBothCtors_UsesStringConstructor()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var json = "\"test-value\"";
        var obj = JsonSerializer.Deserialize<TypeWithBothCtors>(json, options);

        Assert.That(obj, Is.Not.Null);
        Assert.That(obj!.Value, Is.EqualTo("test-value"));
    }

    [Test]
    public void Serialize_MultipleObjects_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var list = new[]
        {
            new TypeWithStringCtor("value1"),
            new TypeWithStringCtor("value2"),
            new TypeWithStringCtor("value3")
        };

        var json = JsonSerializer.Serialize(list, options);
        var deserialized = JsonSerializer.Deserialize<TypeWithStringCtor[]>(json, options);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!, Has.Length.EqualTo(3));
        Assert.That(deserialized[0].Value, Is.EqualTo("value1"));
        Assert.That(deserialized[1].Value, Is.EqualTo("value2"));
        Assert.That(deserialized[2].Value, Is.EqualTo("value3"));
    }

    [Test]
    public void Serialize_UnicodeCharacters_SerializesCorrectly()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(_factory);

        var obj = new TypeWithStringCtor("Test with 日本語 and émojis 🎉");
        var json = JsonSerializer.Serialize(obj, options);
        var deserialized = JsonSerializer.Deserialize<TypeWithStringCtor>(json, options);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.Value, Is.EqualTo(obj.Value));
    }
}
