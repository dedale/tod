using NUnit.Framework;
using System.Net.Mail;
using Tod.Jenkins;
using Tod.Net;

namespace Tod.Tests.Net;

[TestFixture]
internal sealed class MailSenderTests
{
    [Test]
    public async Task Send()
    {
        var config = new MailConfig("noreply@exampe.org", "smtp.local");
        var sender = new MailSender(config);
        var toUser = "user@example.org";
        await sender.Send(toUser, "Subject", "body", "attachment", SendMail).ConfigureAwait(false);

        Task SendMail(MailMessage message)
        {
            Assert.That(message.From?.Address, Is.EqualTo(config.From));
            return Task.CompletedTask;
        }
    }
}
