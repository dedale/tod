using Tod.Core;
using Tod.Jenkins;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Core;

internal sealed class Dummy(List<RequestTestBuildReference> references) : IWithCustomSerialization<Dummy.Serializable>
{
    public List<RequestTestBuildReference> References { get; } = references;

    public Serializable ToSerializable()
    {
        return new Serializable
        {
            References = [.. References.Select(r => r.ToSerializable())]
        };
    }

    internal sealed class Serializable : ICustomSerializable<Dummy>
    {
        public RequestTestBuildReference.Serializable[] References { get; set; } = [];

        public Dummy FromSerializable()
        {
            return new Dummy([.. References.Select(r => r.FromSerializable())]);
        }
    }

    public static Dummy New()
    {
        return new Dummy([
            RequestTestBuildReference.Create(new JobName("MyJob")).Queue().DoneQueued(RandomData.NextBuildNumber),
            RequestTestBuildReference.Create(new JobName("MyTestJob")),
        ]);
    }

    public void SaveNew(string path)
    {
        using var lockedJson = new LockedDummy(this, path, "Save new dummy");
        lockedJson.Value.Save();
    }
}

// Wrapper to simplify usage in tests
internal sealed class LockedDummy : IDisposable
{
    public ILockedJson<Dummy> Value { get; }

    public LockedDummy(Dummy dummy, string path, string reason, TimeSpan? timeout = null, int retryDelayInMs = 100)
    {
        Value = LockedJsonSerializer<Dummy, Dummy.Serializable>.New(dummy, path, reason, true, timeout, retryDelayInMs);
    }

    private LockedDummy(ILockedJson<Dummy> lockedJson)
    {
        Value = lockedJson;
    }

    public static LockedDummy Load(string path, string reason, int retryDelayInMs = 100)
    {
        return new LockedDummy(LockedJsonSerializer<Dummy, Dummy.Serializable>.Load(path, reason, true, retryDelayInMs: retryDelayInMs));
    }

    public static Dummy LoadUnlocked(string path)
    {
        return LockedJsonSerializer<Dummy, Dummy.Serializable>.LoadUnlocked(path);
    }

    public void Save()
    {
        Value.Save();
    }

    public void Dispose()
    {
        Value.Dispose();
    }
}
