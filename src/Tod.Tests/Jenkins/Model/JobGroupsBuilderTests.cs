using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JobGroupsBuilderTests
{
    private static void Test(Action<JobGroupsBuilder>? addMore = null, Action<List<string>>? assertErrors = null)
    {
        using (Assert.EnterMultipleScope())
        {
            var builder = new JobGroupsBuilder();
            builder.AddReferenceRoot(new("MAIN-build"), new("main"), new("build"));
            builder.AddReferenceTest(new("MAIN-tests"), new("main"), new("tests"));
            builder.AddOnDemandRoot(new("CUSTOM-build"), new("build"));
            builder.AddOnDemandTest(new("CUSTOM-tests"), new("tests"));
            (addMore ?? (x => { }))(builder);
            var errors = new List<string>();
            Assert.That(builder.TryBuild(out var jobGroups, errors.Add), Is.True);
            (assertErrors ?? (msgs => Assert.That(msgs, Is.Empty)))(errors);
            Assert.That(jobGroups, Is.Not.Null);
            Debug.Assert(jobGroups is not null);
            Assert.That(jobGroups.ByRoot, Has.Count.EqualTo(1));
            Assert.That(jobGroups.ByRoot[new("build")].ReferenceJobByBranch, Has.Count.EqualTo(1));
            Assert.That(jobGroups.ByRoot[new("build")].ReferenceJobByBranch[new("main")].Value, Is.EqualTo("MAIN-build"));
            Assert.That(jobGroups.ByRoot[new("build")].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));
            Assert.That(jobGroups.ByTest, Has.Count.EqualTo(1));
            Assert.That(jobGroups.ByTest[new("tests")].ReferenceJobByBranch, Has.Count.EqualTo(1));
            Assert.That(jobGroups.ByTest[new("tests")].ReferenceJobByBranch[new("main")].Value, Is.EqualTo("MAIN-tests"));
            Assert.That(jobGroups.ByTest[new("tests")].OnDemandJob.Value, Is.EqualTo("CUSTOM-tests"));
        }
    }

    [Test]
    public void TryBuild_SîmpleCase_Works()
    {
        Test();
    }

    [Test]
    public void TryBuild_MissingOnDemandTestJob_AddError()
    {
        Test(
            builder => builder.AddReferenceTest(new("MAIN-integration-tests"), new("main"), new("integration-tests")),
            errors => Assert.That(errors, Does.Contain("No ondemand job for 'MAIN-integration-tests' job"))
        );
    }

    [Test]
    public void TryBuild_MissingRefTestJob_AddError()
    {
        Test(
            builder => builder.AddOnDemandTest(new("CUSTOM-integration-tests"), new("integration-tests")),
            errors => Assert.That(errors, Does.Contain("No reference job for 'CUSTOM-integration-tests' job"))
        );
    }

    [Test]
    public void TryBuild_NoRoot_AddError()
    {
        var builder = new JobGroupsBuilder();
        var errors = new List<string>();
        Assert.That(() => builder.TryBuild(out var jobGroups, errors.Add), Is.False);
        Assert.That(errors, Is.EquivalentTo(["No root group", "No test group"]));
    }

    [Test]
    public void TryBuild_MissingOndemandRoot_AddError()
    {
        var builder = new JobGroupsBuilder();
        builder.AddReferenceRoot(new("MAIN-build"), new("main"), new("build"));
        var errors = new List<string>();
        Assert.That(() => builder.TryBuild(out var jobGroups, errors.Add), Is.False);
        Assert.That(errors, Does.Contain("No ondemand job for 'MAIN-build' job"));
    }

    [Test]
    public void TryBuild_MissingOndemandRootWithManyRefs_AddError()
    {
        var builder = new JobGroupsBuilder();
        builder.AddReferenceRoot(new("MAIN-build"), new("main"), new("build"));
        builder.AddReferenceRoot(new("PROD-build"), new("prod"), new("build"));
        var errors = new List<string>();
        Assert.That(() => builder.TryBuild(out var jobGroups, errors.Add), Is.False);
        Assert.That(errors, Does.Contain("No ondemand job for 'MAIN-build', 'PROD-build' jobs"));
    }

    [Test]
    public void TryBuild_MissingRefRoot_AddError()
    {
        var builder = new JobGroupsBuilder();
        builder.AddOnDemandRoot(new("CUSTOM-build"), new("build"));
        var errors = new List<string>();
        Assert.That(() => builder.TryBuild(out var jobGroups, errors.Add), Is.False);
        Assert.That(errors, Does.Contain("No reference job for 'CUSTOM-build' job"));
    }

    [Test]
    public void TryBuild_NoTestJobs_AddError()
    {
        var builder = new JobGroupsBuilder();
        builder.AddReferenceRoot(new("MAIN-build"), new("main"), new("build"));
        builder.AddOnDemandRoot(new("CUSTOM-build"), new("build"));
        var errors = new List<string>();
        Assert.That(() => builder.TryBuild(out var jobGroups, errors.Add), Is.False);
        Assert.That(errors, Does.Contain("No test group"));
    }

    [Test]
    public void TryBuild_AddTwoRefRoots_Throws()
    {
        var builder = new JobGroupsBuilder();
        builder.AddReferenceRoot(new("MAIN-build"), new("main"), new("build"));
        Assert.That(() => builder.AddReferenceRoot(new("MAIN-build2"), new("main"), new("build")),
            Throws.ArgumentException.And.Message.EqualTo("Job must be unique, cannot add 'MAIN-build2' job for 'main' branch after 'MAIN-build'"));
    }

    [Test]
    public void TryBuild_AddTwoOnDemandRoots_Throws()
    {
        var builder = new JobGroupsBuilder();
        builder.AddOnDemandRoot(new("CUSTOM-build"), new("build"));
        Assert.That(() => builder.AddOnDemandRoot(new("CUSTOM-build2"), new("build")),
            Throws.ArgumentException.And.Message.EqualTo("Job must be unique, cannot add 'CUSTOM-build2' job after 'CUSTOM-build' (Parameter 'job')"));
    }

    [Test]
    public void TryBuild_AddTwoRefTests_Throws()
    {
        var builder = new JobGroupsBuilder();
        builder.AddReferenceTest(new("MAIN-tests"), new("main"), new("tests"));
        Assert.That(() => builder.AddReferenceTest(new("MAIN-tests2"), new("main"), new("tests")),
            Throws.ArgumentException.And.Message.EqualTo("Job must be unique, cannot add 'MAIN-tests2' job for 'main' branch after 'MAIN-tests'"));
    }

    [Test]
    public void TryBuild_AddTwoOnDemandTests_Throws()
    {
        var builder = new JobGroupsBuilder();
        builder.AddOnDemandTest(new("CUSTOM-tests"), new("tests"));
        Assert.That(() => builder.AddOnDemandTest(new("CUSTOM-tests2"), new("tests")),
            Throws.ArgumentException.And.Message.EqualTo("Job must be unique, cannot add 'CUSTOM-tests2' job after 'CUSTOM-tests' (Parameter 'job')"));
    }
}
