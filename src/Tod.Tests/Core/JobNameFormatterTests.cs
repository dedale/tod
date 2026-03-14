using NUnit.Framework;
using Tod.Core;
using Tod.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class JobNameFormatterTests
{
    [TestCase("", "")]
    [TestCase("/", "*[90m/*[38;5;0045m*[0m")]
    [TestCase("simple-job", "simple-job")]
    [TestCase("folder/job-name", "*[90mfolder/*[38;5;0045mjob-name*[0m")]
    [TestCase("folder1/folder2/folder3/job-name", "*[90mfolder1/folder2/folder3/*[38;5;0045mjob-name*[0m")]
    [TestCase("folder/", "*[90mfolder/*[38;5;0045m*[0m")]
    [TestCase("org/team/project/branch/feature/sub-feature/build-job", "*[90morg/team/project/branch/feature/sub-feature/*[38;5;0045mbuild-job*[0m")]
    public void Format_Works(string name, string formatted)
    {
        Assert.That(JobNameFormatter.Format(new JobName(name)), Is.EqualTo(formatted.Replace("*", "\x1b")));
    }
}
