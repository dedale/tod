using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed record ReferenceChain(
    BuildReference RootBuild,
    bool RootBuildSucceeded,
    Dictionary<JobName, RefTestBuildReference> TestBuilds,
    bool ReportSent = false
) : IWithCustomSerialization<ReferenceChain.Serializable>
{
    public bool AllTestsDone => TestBuilds.Values.All(tb => tb.IsDone);

    public ReferenceChain MarkTestDone(JobName testJob, BuildReference testBuild)
    {
        if (!TestBuilds.ContainsKey(testJob))
        {
            return this;
        }

        var updated = new Dictionary<JobName, RefTestBuildReference>(TestBuilds)
        {
            [testJob] = TestBuilds[testJob].DoneReference(testBuild.BuildNumber)
        };
        return this with { TestBuilds = updated };
    }

    public ReferenceChain MarkReportSent()
    {
        return this with { ReportSent = true };
    }

    internal sealed class Serializable : ICustomSerializable<ReferenceChain>
    {
        [JsonConstructor]
        private Serializable(
            BuildReference rootBuild,
            bool rootBuildSucceeded,
            List<RefTestBuildReference.Serializable> testBuilds,
            bool reportSent)
        {
            RootBuild = rootBuild;
            RootBuildSucceeded = rootBuildSucceeded;
            TestBuilds = testBuilds;
            ReportSent = reportSent;
        }

        public Serializable(ReferenceChain chain)
        {
            RootBuild = chain.RootBuild;
            RootBuildSucceeded = chain.RootBuildSucceeded;
            TestBuilds = [.. chain.TestBuilds.Values.Select(b => b.ToSerializable())];
            ReportSent = chain.ReportSent;
        }

        public BuildReference RootBuild { get; set; }
        public bool RootBuildSucceeded { get; set; }
        public List<RefTestBuildReference.Serializable> TestBuilds { get; set; }
        public bool ReportSent { get; set; }

        public ReferenceChain FromSerializable()
        {
            return new ReferenceChain(
                RootBuild,
                RootBuildSucceeded,
                TestBuilds.Select(b => b.FromSerializable()).ToDictionary(b => b.JobName),
                ReportSent
            );
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}

// Tracks the state of root builds and their associated test builds for a chain, and determines when reports are ready to be sent.
internal sealed class ChainReportTracker(string chain, List<ReferenceChain> referenceChains, IByChainStore byChainStore)
{
    public ChainReportTracker(string chain, IByChainStore byChainStore)
        : this(chain, [], byChainStore)
    {
    }

    private void Save()
    {
        byChainStore.Save(chain, ToSerializable());
    }

    public void AddRootBuild(RootBuild rootBuild, JobName[] expectedTestJobs)
    {
        var testBuilds = expectedTestJobs.ToDictionary(
            job => job,
            RefTestBuildReference.Create
        );
        referenceChains.Add(new ReferenceChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds));
        Save();
    }

    public async Task MarkTestDone(int rootBuildNumber, JobName testJob, BuildReference testBuild, Func<Task> sendReport)
    {
        var index = referenceChains.FindIndex(c => c.RootBuild.BuildNumber == rootBuildNumber);
        if (index >= 0)
        {
            referenceChains[index] = referenceChains[index].MarkTestDone(testJob, testBuild);

            if (referenceChains[index].AllTestsDone && !referenceChains[index].ReportSent)
            {
                await sendReport().ConfigureAwait(false);
                referenceChains[index] = referenceChains[index].MarkReportSent();
            }

            Save();
        }
    }

    public ReferenceChain[] GetReadyForReport()
    {
        var index = referenceChains.FindIndex(rb => rb.AllTestsDone && !rb.ReportSent);
        if (index < 0)
        {
            return [];
        }

        var buildsToReport = new List<ReferenceChain> { referenceChains[index] };
        for (int i = index - 1; i >= 0; i--)
        {
            if (!referenceChains[i].RootBuildSucceeded)
            {
                buildsToReport.Insert(0, referenceChains[i]);
            }
            else
            {
                break;
            }
        }

        return [.. buildsToReport];
    }

    [method: JsonConstructor]
    internal sealed class Serializable(string chain, List<ReferenceChain.Serializable> referenceChains)
    {
        public string Chain { get; } = chain;
        public List<ReferenceChain.Serializable> ReferenceChains { get; } = referenceChains;

        public ChainReportTracker FromSerializable(IByChainStore byChainStore)
        {
            var chains = ReferenceChains.Select(rc => rc.FromSerializable()).ToList();
            return new ChainReportTracker(Chain, chains, byChainStore);
        }
    }

    public Serializable ToSerializable()
    {
        var serializableChains = referenceChains.Select(rc => rc.ToSerializable()).ToList();
        return new Serializable(chain, serializableChains);
    }
}
