using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tod.Jenkins;

namespace Tod.Core;

internal interface ILockedJson<T> : IDisposable
{
    T Value { get; }
    void Save();
    DateTime LastModifiedUtc { get; }
    Task<T> Update(Func<T, Task<T>> update);
}

internal sealed class LockedJsons<T> : IEnumerable<ILockedJson<T>>, IDisposable
{
    private readonly List<ILockedJson<T>> _items = [];

    public void Add(ILockedJson<T> item)
    {
        _items.Add(item);
    }

    public void Dispose()
    {
        foreach (var disposable in _items)
        {
            disposable.Dispose();
        }
        _items.Clear();
    }

    public IEnumerator<ILockedJson<T>> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    [ExcludeFromCodeCoverage]
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count => _items.Count;

    public ILockedJson<T> this[int index] => _items[index];
}

internal static class LockedJsonSerializer<TValue, TSerializable>
    where TValue : IWithCustomSerialization<TSerializable>
    where TSerializable : ICustomSerializable<TValue>
{
    private static readonly TimeSpan s_defaultLockTimeout = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions s_jsonOptionsIndented = GetJsonOptions(true);
    private static readonly JsonSerializerOptions s_jsonOptionsFlat = GetJsonOptions(false);

    public static JsonSerializerOptions GetJsonOptions(bool indented)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = indented,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new SingleStringValueConverterFactory());
        return options;
    }

    private static FileLock NewFileLock(string path, string reason, TimeSpan? timeout = null, int retryDelayInMs = 100)
    {
        var stopwatch = Stopwatch.StartNew();
        timeout ??= s_defaultLockTimeout;
        while (stopwatch.Elapsed < timeout)
        {
            try
            {
                return new FileLock(path, reason);
            }
            catch (AlreadyLockedException)
            {
                Thread.Sleep(retryDelayInMs);
            }
        }
        throw new TimeoutException($"Timeout waiting for lock on '{path}'");
    }

    public static ILockedJson<TValue> New(TValue value, string path, string reason, bool indented = false, TimeSpan? timeout = null, int retryDelayInMs = 100)
    {
        var fileLock = NewFileLock(path, reason, timeout, retryDelayInMs);
        return new LockedJson(path, fileLock, value, indented);
    }

    public static ILockedJson<TValue> Load(string path, string reason, bool indented = false, TimeSpan? timeout = null, int retryDelayInMs = 100)
    {
        var fileLock = NewFileLock(path, reason, timeout, retryDelayInMs);
        var json = File.ReadAllText(path, Encoding.UTF8);
        // Ignore indentation when reading
        var serializable = JsonSerializer.Deserialize<TSerializable>(json, s_jsonOptionsFlat);
        if (serializable == null)
        {
            fileLock.Dispose();
            throw new InvalidOperationException($"Cannot deserialize {typeof(TSerializable).FullName} from '{path}'");
        }
        return new LockedJson(path, fileLock, serializable.FromSerializable(), indented);
    }

    public static TValue LoadUnlocked(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);
        // Ignore indentation when reading
        var serializable = JsonSerializer.Deserialize<TSerializable>(json, s_jsonOptionsFlat);
        if (serializable == null)
        {
            throw new InvalidOperationException($"Cannot deserialize {typeof(TSerializable).FullName} from '{path}'");
        }
        return serializable.FromSerializable();
    }

    private sealed class LockedJson(string path, FileLock fileLock, TValue value, bool indented) : ILockedJson<TValue>
    {
        public TValue Value => value;

        public void Save()
        {
            var json = JsonSerializer.Serialize(Value.ToSerializable(), indented ? s_jsonOptionsIndented : s_jsonOptionsFlat);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json, Encoding.UTF8);
        }

        public DateTime LastModifiedUtc => File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

        public async Task<TValue> Update(Func<TValue, Task<TValue>> update)
        {
            value = await update(Value).ConfigureAwait(false);
            Save();
            return Value;
        }

        public void Dispose()
        {
            fileLock.Dispose();
        }
    }
}
