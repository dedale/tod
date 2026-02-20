using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed record BaselineChain(
    BuildReference RootBuild,
    bool RootBuildSucceeded,
    Dictionary<JobName, BaseTestBuildReference> TestBuilds,
    bool ReportSent = false
) : IWithCustomSerialization<BaselineChain.Serializable>
{
    public bool AllTestsDone => TestBuilds.Values.All(tb => tb.IsDone);

    public BaselineChain MarkTestDone(JobName testJob, BuildReference testBuild)
    {
        if (!TestBuilds.ContainsKey(testJob))
        {
            return this;
        }

        var updated = new Dictionary<JobName, BaseTestBuildReference>(TestBuilds)
        {
            [testJob] = TestBuilds[testJob].DoneBaseline(testBuild.BuildNumber)
        };
        return this with { TestBuilds = updated };
    }

    public BaselineChain MarkReportSent()
    {
        return this with { ReportSent = true };
    }

    internal sealed class Serializable : ICustomSerializable<BaselineChain>
    {
        [JsonConstructor]
        private Serializable(
            BuildReference rootBuild,
            bool rootBuildSucceeded,
            List<BaseTestBuildReference.Serializable> testBuilds,
            bool reportSent)
        {
            RootBuild = rootBuild;
            RootBuildSucceeded = rootBuildSucceeded;
            TestBuilds = testBuilds;
            ReportSent = reportSent;
        }

        public Serializable(BaselineChain chain)
        {
            RootBuild = chain.RootBuild;
            RootBuildSucceeded = chain.RootBuildSucceeded;
            TestBuilds = [.. chain.TestBuilds.Values.Select(b => b.ToSerializable())];
            ReportSent = chain.ReportSent;
        }

        public BuildReference RootBuild { get; set; }
        public bool RootBuildSucceeded { get; set; }
        public List<BaseTestBuildReference.Serializable> TestBuilds { get; set; }
        public bool ReportSent { get; set; }

        public BaselineChain FromSerializable()
        {
            return new BaselineChain(
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
internal sealed class ChainReportTracker(string chain, List<BaselineChain> baselineChains, IByChainStore byChainStore)
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
            BaseTestBuildReference.Create
        );
        baselineChains.Add(new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds));
        Save();
    }

    public async Task MarkTestDone(int rootBuildNumber, JobName testJob, BuildReference testBuild, Func<Task> sendReport)
    {
        var index = baselineChains.FindIndex(c => c.RootBuild.BuildNumber == rootBuildNumber);
        if (index >= 0)
        {
            baselineChains[index] = baselineChains[index].MarkTestDone(testJob, testBuild);

            if (baselineChains[index].AllTestsDone && !baselineChains[index].ReportSent)
            {
                await sendReport().ConfigureAwait(false);
                baselineChains[index] = baselineChains[index].MarkReportSent();
            }

            Save();
        }
    }

    public BaselineChain[] GetReadyForReport()
    {
        var index = baselineChains.FindIndex(rb => rb.AllTestsDone && !rb.ReportSent);
        if (index < 0)
        {
            return [];
        }

        var buildsToReport = new List<BaselineChain> { baselineChains[index] };
        for (int i = index - 1; i >= 0; i--)
        {
            if (!baselineChains[i].RootBuildSucceeded)
            {
                buildsToReport.Insert(0, baselineChains[i]);
            }
            else
            {
                break;
            }
        }

        return [.. buildsToReport];
    }

    [method: JsonConstructor]
    internal sealed class Serializable(string chain, List<BaselineChain.Serializable> baselineChains)
    {
        public string Chain { get; } = chain;
        public List<BaselineChain.Serializable> BaselineChains { get; } = baselineChains;

        public ChainReportTracker FromSerializable(IByChainStore byChainStore)
        {
            var chains = BaselineChains.Select(rc => rc.FromSerializable()).ToList();
            return new ChainReportTracker(Chain, chains, byChainStore);
        }
    }

    public Serializable ToSerializable()
    {
        var serializableChains = baselineChains.Select(rc => rc.ToSerializable()).ToList();
        return new Serializable(chain, serializableChains);
    }
}
