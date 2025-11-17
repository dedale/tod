using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class TriggerConfigTests
{
    [Test]
    public void GetParameter_InvalidParameter_Throws()
    {
        var config = new TriggerConfig(OnDemandJobKind.Root, (TriggerParameter)999, "UNKNOWN");
        var triggerParameters = new TriggerParameters(RandomData.NextSha1(), RandomData.NextBuildNumber);
        Assert.That(() => config.GetParameter(triggerParameters), Throws.TypeOf<NotSupportedException>());
    }

    [Test]
    public void GetParameter_MissingBuildNumberForBuildSelector_Throws()
    {
        var config = new TriggerConfig(OnDemandJobKind.Root, TriggerParameter.BuildSelector, "BUILD_SELECTOR");
        var triggerParameters = new TriggerParameters(RandomData.NextSha1(), null);
        Assert.That(() => config.GetParameter(triggerParameters),
            Throws.InvalidOperationException.And.Message.EqualTo("Upstream build number is required for BuildSelector trigger parameter"));
    }
}
