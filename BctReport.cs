using System.Net.Mail;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService;
using Smtp;
using Smtp.Models;

#pragma warning disable CA2254

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
    private readonly BctDbContext _context = new (new DbContextOptions<BctDbContext>());

    /// <summary>
    /// Sends the report template reminder email.
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
                .Select(es => new { es.SmtpServer, es.Port, es.Sender, es.Password, es.IsLive, es.TestAddress })
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
            //Get the email template.
            var emailTemplate = _context.EmailTemplates
                .Where(et => et.Id == rptReminderData.EmailTemplateId && et.Active == true)
                .Select(et => new { et.Template, et.Subject })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {rptReminderData.EmailTemplateId}");

            // Replace the email template & Subject [data] with the actual data.
            var emailBody = emailTemplate.Template;//TODO: Replace the email template [data] with the actual data.
            var emailSubject = emailTemplate.Subject;//TODO: Replace the email subject [data] with the actual data.

            // Get the recipients.
            var recipients = Recipients(rptTemplateSettingsData.Client_Id, clientSettingsData.Code!);
var recipientsString = string.Join(",", recipients);

            // Create the EMAIL message settings object.
            MailMessageSettings mailMessageSettings = new()
        {
            To = emailSettings.IsLive ? recipientsString : $"{emailSettings.TestAddress!}, chris.tate@b2Gnow.com",
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

    private List<string> Recipients(int clientId, string tenantCode)
    {
        try
        {
            // Step 1: Get the user IDs matching the conditions from the Security_User table
            var userIds = _context.SecurityUsers
                .Where(u => u.TenantCode == tenantCode && u.Client_Id == clientId && u.Active)
                .Select(u => u.Id)
                .ToList();

            // Step 2: Get the role ID for the role with DisplayText 'Submit Reporting'
            var roleId = _context.SecurityRoles
                .Where(r => r.DisplayText == "Submit Reporting")
                .Select(r => r.Id)
                .FirstOrDefault();

            // Step 3: Get the user IDs associated with the role ID from the Security_UserRole table
            var userIdsInRole = _context.SecurityUserRoles
                .Where(ur => userIds.Contains(ur.UserId) && ur.RoleId == roleId)
                .Select(ur => ur.UserId)
                .ToList();

            // Step 4: Get the emails of users with IDs from the previous step
             List<string> userEmails = _context.SecurityUsers
                 .Where(u => userIdsInRole.Contains(u.Id!))
                 .Select(u => u.Email)
                 .ToList()!;

        return userEmails;
        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
            return [];
        }
    }
}
