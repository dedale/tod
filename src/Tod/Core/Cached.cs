using Tod.Jenkins;

namespace Tod.Core;

internal sealed class Cached<TValue, TSerializable>
    where TValue : IWithCustomSerialization<TSerializable>
    where TSerializable : ICustomSerializable<TValue>
{
    private TValue _cached;
    private readonly string _path;
    private DateTime _timestampUtc;

    private Cached(ILockedJson<TValue> lockedJson, string path)
    {
        _cached = lockedJson.Value;
        _path = path;
        _timestampUtc = File.GetLastWriteTimeUtc(path);
        lockedJson.Dispose();
    }

    public static Cached<TValue, TSerializable> New(TValue value, string path)
    {
        using var lockedJson = LockedJsonSerializer<TValue, TSerializable>.New(value, path, "Cached New", false);
        lockedJson.Save();
        return new Cached<TValue, TSerializable>(lockedJson, path);
    }

    public Cached(string path)
        : this(LockedJsonSerializer<TValue, TSerializable>.Load(path, "Cached Ctor", false), path)
    {
    }

    public TValue Value
    {
        get
        {
            if (File.GetLastWriteTimeUtc(_path) > _timestampUtc)
            {
                using var lockedJson = LockedJsonSerializer<TValue, TSerializable>.Load(_path, "Cached Load", false);
                _cached = lockedJson.Value;
                _timestampUtc = File.GetLastWriteTimeUtc(_path);
            }
            return _cached;
        }
    }

    public ILockedJson<TValue> Lock(string reason)
    {
        return LockedJsonSerializer<TValue, TSerializable>.Load(_path, reason, false);
    }
}
