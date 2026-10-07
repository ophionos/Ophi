using MailKit.Net.Smtp;

namespace Ophi.Infrastructure.Email;

public interface ISmtpClientFactory
{
    ISmtpClient Create();
}

public class SmtpClientFactory : ISmtpClientFactory
{
    public ISmtpClient Create() => new SmtpClient { Timeout = 15_000 };
}
