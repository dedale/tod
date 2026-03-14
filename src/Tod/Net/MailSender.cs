using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Mail;
using System.Text;
using Tod.Jenkins;

namespace Tod.Net;

internal interface IMailSender
{
    Task Send(string to, string subject, string body, string attachment);
}

internal sealed class MailSender(MailConfig config) : IMailSender
{
    [ExcludeFromCodeCoverage]
    public Task Send(string to, string subject, string body, string attachment)
    {
        return Send(to, subject, body, attachment, null);
    }

    public Task Send(string to, string subject, string body, string attachment, Func<MailMessage, Task>? send)
    {
        var mail = new MailMessage(config.From, to)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = true,
        };

        var bytes = Encoding.UTF8.GetBytes(attachment);
        var stream = new MemoryStream(bytes);
        mail.Attachments.Add(new Attachment(stream, "report.html", "text/html"));

        return Send(mail, send);
    }

    internal static SmtpClient GetSmtpClient(MailConfig config)
    {
        var client = new SmtpClient(config.SmtpHost, config.SmtpPort);
        client.EnableSsl = config.EnableSsl;
        if (!string.IsNullOrEmpty(config.User))
        {
            client.Credentials = new NetworkCredential(config.User, config.Password);
        }
        return client;
    }

    [ExcludeFromCodeCoverage]
    private Task Send(MailMessage mail, Func<MailMessage, Task>? send = null)
    {
        return (send ?? (GetSmtpClient(config).SendMailAsync))(mail);
    }
}
