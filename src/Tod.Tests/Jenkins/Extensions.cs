using NUnit.Framework;
using System.Text.Json;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal static class DateTimeExtensions
{
    public static DateTime TruncateSeconds(this DateTime dt)
    {
        return new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, dt.Kind);
    }
}

internal static class JsonExtensions
{
    private static readonly JsonSerializerOptions _options = new()
    {
        //  Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static JsonDocument Serialize(this object obj)
    {
        var json = JsonSerializer.Serialize(obj, _options);
        return JsonDocument.Parse(json);
    }
}

internal static class ListExtensions
{
    private static readonly Random s_random = new();

    public static void Shuffle<T>(this IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = s_random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}

internal static class SerializableExtensions
{
    public static void AssertSerializable<T>(this T obj) where T : IEquatable<T>
    {
        var json = JsonSerializer.Serialize(obj);
        var clone = JsonSerializer.Deserialize<T>(json);
        Assert.That(clone, Is.EqualTo(obj));
    }
}

internal static class WithCustomSerializationExtensions
{
    internal static TCustom SerializationRoundTrip<TCustom, TSerializable>(this TCustom custom)
        where TCustom : IWithCustomSerialization<TSerializable>
        where TSerializable : ICustomSerializable<TCustom>
    {
        var json = JsonSerializer.Serialize(custom.ToSerializable());
        var clone = JsonSerializer.Deserialize<TSerializable>(json)!.FromSerializable();
        return clone;
    }
}
