using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using Tod.Jenkins;

namespace Tod.Net;

internal interface IMailSender
{
    Task Send(string to, string subject, string body);
}

internal sealed class MailSender(MailConfig config) : IMailSender
{
    [ExcludeFromCodeCoverage]
    public Task Send(string to, string subject, string body)
    {
        return Send(to, subject, body, null);
    }

    public Task Send(string to, string subject, string body, Func<MailMessage, Task>? send)
    {
        var mail = new MailMessage(config.From, to)
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        return Send(mail, send);
    }

    [ExcludeFromCodeCoverage]
    private Task Send(MailMessage mail, Func<MailMessage, Task>? send = null)
    {
        return (send ?? (new SmtpClient(config.SmtpHost).SendMailAsync))(mail);
    }
}
