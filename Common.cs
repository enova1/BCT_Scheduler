using DataAccess;
using DataAccess.BctModels;
using Microsoft.EntityFrameworkCore;
using Smtp.Models;
using System.Net.Mail;
using Smtp;

namespace BCT_Scheduler;

public class Common
{
    private readonly IEmailService _emailService = new EmailService();
    private readonly BctDbContext _context = new(new DbContextOptions<BctDbContext>());

    public (bool, string) SendEmail(string tenantCode, string recipientsString, string emailSubject, string emailBody, string priority = "0" )
    {
        //Get the email settings.
        var emailSettings = _context.EmailSettings
            .Where(es => es.TenantCode == tenantCode && es.Active)
            .Select(es => new { es.SmtpServer, es.Port, es.Sender, es.Password, es.IsLive, es.TestAddress, es.UserName })
            .FirstOrDefault() ?? throw new Exception($"Email settings not found for {tenantCode}.");

        // Create SMTP server settings object.
        SmtpServerSettings smtpServerSettings = new()
        {
            UserName = emailSettings.UserName!,
            SmtpServer = emailSettings.SmtpServer!,
            Port = emailSettings.Port,
            SenderEmail = string.IsNullOrEmpty(emailSettings.Sender) ? "system@blackcattransit.com" : emailSettings.Sender,
            SenderPassword = emailSettings.Password!
        };

        //Get the admin client settings.
        var clientSettings = _context.ClientSettings
            .Where(es => es.ClientCode == tenantCode)
            .Select(es => new { es.SupportEmail })
            .FirstOrDefault() ?? throw new Exception($"ClientSettings settings not found for {tenantCode}.");

        // Create the EMAIL message settings object.
        MailMessageSettings mailMessageSettings = new()
        {
            To = emailSettings.IsLive ? recipientsString : $"{emailSettings.TestAddress!}, chris.tate@b2Gnow.com",
            From = clientSettings.SupportEmail,
            Sender = smtpServerSettings.SenderEmail,
            Subject = emailSubject,
            Body = emailBody,
            IsBodyHtml = true,
            Priority = priority.Equals("2") ? MailPriority.High : MailPriority.Normal
        };

        //`Send the email.
        var emailResult = _emailService.SendEmail(mailMessageSettings, smtpServerSettings);
        if (!emailResult.Item1)
        {
            return (false, emailResult.Item2);
        }

        //save email to email_settings table
        var sentSaved = UpdateEmailSetting(mailMessageSettings, tenantCode, emailResult.Item1);
        return !sentSaved.Item1 ? (false, emailResult.Item2) : (true, "Email sent successfully");
    }

    private (bool, string) UpdateEmailSetting(MailMessageSettings mailMessageSettings, string tenantCode, bool sent)
    {
        using var transaction = _context.Database.BeginTransaction();
        try
        {
            SystemEmail email = new()
            {
                Recepient = mailMessageSettings.To,
                Subject = mailMessageSettings.Subject,
                Body = mailMessageSettings.Body,
                Sent = sent,
                TenantCode = tenantCode,
                Active = true,
                CreatedDate = DateTime.Now,
                EmailAttachments = []
            };
            // Disable the trigger
            _context.Database.ExecuteSqlRaw("DISABLE TRIGGER [dbo].[Email_SystemEmails_Insert] ON [dbo].[Email_SystemEmails]");

            // insert of new row
            _context.SystemEmails.Add(email);
            _context.SaveChanges();

            transaction.Commit();
            // Re-enable the trigger
            _context.Database.ExecuteSqlRaw("ENABLE TRIGGER [dbo].[Email_SystemEmails_Insert] ON [dbo].[Email_SystemEmails]");


            return (true, "Save successful");
        }
        catch (Exception ex)
        {
            // Rollback transaction on error
            transaction.Rollback();
            // Re-enable the trigger
            _context.Database.ExecuteSqlRaw("ENABLE TRIGGER [dbo].[Email_SystemEmails_Insert] ON [dbo].[Email_SystemEmails]");
            // Log the exception or handle it as needed
            return (false, "Error saving sent email: " + ex.Message);
        }
    }


}