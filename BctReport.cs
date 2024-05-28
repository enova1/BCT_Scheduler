using System.Net.Mail;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService;
using Smtp;
using Smtp.Models;

namespace BCT_Scheduler;

/// <summary>
/// Custom Report Reminder class for sending email reminders.
/// </summary>
/// <remarks>
/// Custom Report Reminder class for sending email reminders.
/// </remarks>
/// <param name="timeZone"></param>
public class BctReport(ILogger<BctReport> log, string timeZone = "Eastern Standard Time")
{
    private readonly EmailService _emailService = new ();
    private readonly Notify _notify = new(new LoggerFactory().CreateLogger<Notify>());
    private readonly DateTime _estDateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
//    
    private readonly BctDbContext _context = new (new DbContextOptions<BctDbContext>());

    /// <summary>
    /// 
    /// </summary>
    /// <param name="reminderId">Which reminder to send</param>
    /// <param name="month">The month to send the email.</param>
    public void SendReminderEmail(int reminderId, string month)
    {
        try
        {
            //Get the report reminders.
            var rptReminderData = _context.ReportReminders.FirstOrDefault(rr => rr.Id == reminderId && rr.Active == true) ?? throw new Exception($"Report Reminder was not found. {reminderId}");
            var beforeAfter = rptReminderData.WhenToSend == "1" ? "Before" : "After";

            //Get the report template settings.
            var rptTemplateSettingsData = _context.ReportingProfileTemplates
                .Where(rts => rts.Id == rptReminderData.ReportTemplateId && rts.Active)
                .Select(rts => new { rts.DisplayName, rts.Client_Id })
                .FirstOrDefault() ?? throw new Exception($"Report Template Settings were not found. {rptReminderData.ReportTemplateId}");

            //Get the client settings.
            var clientSettingsData = _context.Clients
                .Where(ac => ac.Id == rptTemplateSettingsData.Client_Id && ac.Active == true)
                .Select(ac => new { ac.Code })
                .FirstOrDefault() ?? throw new Exception($"Client Settings not found for {rptTemplateSettingsData.Client_Id}.");

            //Get the email settings.
            var emailSettings = _context.EmailSettings
                .Where(es => es.TenantCode == clientSettingsData.Code && es.Active == true)
                .Select(es => new { es.SmtpServer, es.Port, es.Sender, es.Password })
                .FirstOrDefault() ?? throw new Exception($"Email settings not found for {clientSettingsData.Code}.");

            // Create SMTP server settings object.
                SmtpServerSettings smtpServerSettings = new()
            {
                SmtpServer = emailSettings.SmtpServer!,
                Port = emailSettings.Port,
                SenderEmail = emailSettings.Sender!,
                SenderPassword = emailSettings.Password!
            };

            //Get the admin client settings.
            var clientSettings = _context.ClientSettings
                .Where(es => es.ClientCode == clientSettingsData.Code)
                .Select(es => new { es.SupportEmail })
                .FirstOrDefault() ?? throw new Exception($"ClientSettings settings not found for {clientSettingsData.Code}.");

            var emailTemplate = _context.EmailTemplates
                .Where(et => et.Id == rptReminderData.EmailTemplateId && et.Active == true)
                .Select(et => new { et.Template, et.Subject })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {rptReminderData.EmailTemplateId}");

            var emailBody = emailTemplate.Template;//TODO: Replace the email template [data] with the actual data.
            var emailSubject = emailTemplate.Subject;//TODO: Replace the email subject [data] with the actual data.

            // Create the EMAIL message settings object.
            MailMessageSettings mailMessageSettings = new()
        {
            To = "Chris.tate@b2Gnow.com", //TODO: Replace with the actual recipient email address.
                From = clientSettings.SupportEmail,
            Sender = emailSettings.Sender!,
            Subject = emailSubject!,
            Body = emailBody!,
            IsBodyHtml = true,
            Priority = rptReminderData.WhenToSend == "2" ? MailPriority.High : MailPriority.Normal
            };

            // Attempt to send the email.
             var emailResult =  _emailService.SendEmail(mailMessageSettings, smtpServerSettings);
            if (!emailResult.Item1)
            {
                // Log the email was NOT sent
                _notify.ProcessingCompletion($"FAILED client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
            }

            // Log the email was sent
_notify.ProcessingCompletion($"SUCCESS client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");

        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
        }
    }

    private void SendEmail(int reminderId, string month)
    {
    // Create SMTP server settings object.
                SmtpServerSettings smtpServerSettings = new()
            {
                SmtpServer = "mail.smtp2go.com",
                Port = 587,
                SenderEmail = "system@blackcattransit.com",
                SenderPassword = "n0GSqaXCdEFdNQDV"
                };
        // Create a MailMessage object
        MailMessageSettings mailMessageSettings = new()
        {
            To = "chris.tate@b2gnow.com",
            From = "system@blackcattransit.com",
            Subject = "Test Email Sent from Hangfire Scheduler",
            Body = $"This is a test email.</br> ReminderId({reminderId}) </br> Month:({month}) </br> Date:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}",
            IsBodyHtml = true,
            Priority = MailPriority.Normal,
            Sender = "system@blackcattransit.com"
        };

        var emailResult = _emailService.SendEmail(mailMessageSettings, smtpServerSettings);
if (!emailResult.Item1)
{
    // Log the email was NOT sent
    _notify.ProcessingCompletion($"FAILED clientId:name:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
    return;
}

_notify.ProcessingCompletion($"SUCCESS clientId:name:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
    }
}
