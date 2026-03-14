using NUnit.Framework;
using System.Net;
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
        var config = new MailConfig("noreply@example.org", "smtp.local");
        var sender = new MailSender(config);
        var toUser = "user@example.org";
        await sender.Send(toUser, "Subject", "body", "attachment", SendMail).ConfigureAwait(false);

        Task SendMail(MailMessage message)
        {
            Assert.That(message.From?.Address, Is.EqualTo(config.From));
            return Task.CompletedTask;
        }
    }

    [Test]
    public void GetSmtpClient_SetsHostPortAndSsl()
    {
        var config = new MailConfig("from@test.com", "smtp.test.com", 587, enableSsl: true);

        var client = MailSender.GetSmtpClient(config);

        Assert.That(client.Host, Is.EqualTo(config.SmtpHost));
        Assert.That(client.Port, Is.EqualTo(config.SmtpPort));
        Assert.That(client.EnableSsl, Is.EqualTo(config.EnableSsl));
    }

    [Test]
    public void GetSmtpClient_SetsCredentials_WhenUserIsProvided()
    {
        var config = new MailConfig("from@test.com", "smtp.test.com", user: "user", password: "password");

        var client = MailSender.GetSmtpClient(config);

        var credentials = client.Credentials as NetworkCredential;
        Assert.That(credentials?.UserName, Is.EqualTo(config.User));
        Assert.That(credentials?.Password, Is.EqualTo(config.Password));
    }

    [Test]
    public void GetSmtpClient_DoesNotSetCredentials_WhenUserIsEmpty()
    {
        var config = new MailConfig("from@test.com", "smtp.test.com");

        var client = MailSender.GetSmtpClient(config);

        Assert.That(client.Credentials, Is.Null);
    }
}
