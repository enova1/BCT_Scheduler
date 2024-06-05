using System.Net.Mail;
using DataAccess;
using DataAccess.BctModels;
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
    private readonly Notify _notify = new();
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
                .Select(rts => new { rts.DisplayName, rts.Client_Id, rts.Id, rts.Status })
                .FirstOrDefault() ?? throw new Exception($"Report Template Settings were not found. {rptReminderData.ReportTemplateId}");

            //Only send the email if the report template is published.
            var strEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development" ? "In Development" : "Published"; //"In Development
            if (rptTemplateSettingsData.Status != strEnv) return;

            //Get the client settings.
            var clientSettingsData = _context.Clients
                .Where(ac => ac.Id == rptTemplateSettingsData.Client_Id && ac.Active == true)
                .Select(ac => new { ac.Code })
                .FirstOrDefault() ?? throw new Exception($"Client Settings not found for {rptTemplateSettingsData.Client_Id}.");

            //Get the email settings.
            var emailSettings = _context.EmailSettings
                .Where(es => es.TenantCode == clientSettingsData.Code && es.Active == true)
                .Select(es => new { es.SmtpServer, es.Port, es.Sender, es.Password, es.IsLive, es.TestAddress, es.UserName })
                .FirstOrDefault() ?? throw new Exception($"Email settings not found for {clientSettingsData.Code}.");

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
                .Where(es => es.ClientCode == clientSettingsData.Code)
                .Select(es => new { es.SupportEmail })
                .FirstOrDefault() ?? throw new Exception($"ClientSettings settings not found for {clientSettingsData.Code}.");

            //Get the email template.
            var notificationTypeId = _context.EmailNotificationTypes
                .Where(ent => ent.NotificationType == rptReminderData.EmailNotificationType && ent.ClientCode == clientSettingsData.Code)
                .Select(ent => new { ent.Id })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {rptReminderData.EmailTemplateId}");

            var emailTemplate = _context.EmailTemplates
                .Where(et => Equals(et.EmailType_Id, notificationTypeId.Id))
                .Select(et => new { et.Template, et.Subject })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {rptReminderData.EmailTemplateId}");

            // Replace the email template & Subject [data] with the actual data.
            var emailBody = emailTemplate.Template!
                .Replace("[StatusReportDisplayName]", rptTemplateSettingsData.DisplayName)
                .Replace("[numberofdays]", rptReminderData.NumberOfDays.ToString());
            var emailSubject = emailTemplate.Subject!
                .Replace("[Reporting_Display_Name]", rptTemplateSettingsData.DisplayName);

            // Get the recipients.
            var recipients = Recipients(rptTemplateSettingsData.Id);
            var recipientsString = string.Join(",", recipients);

            // Create the EMAIL message settings object.
            MailMessageSettings mailMessageSettings = new()
            {
                To = emailSettings.IsLive ? recipientsString : $"{emailSettings.TestAddress!}, chris.tate@b2Gnow.com",
                From = clientSettings.SupportEmail,
                Sender = smtpServerSettings.SenderEmail,
                Subject = emailSubject,
                Body = emailBody,
                IsBodyHtml = true,
                Priority = rptReminderData.WhenToSend.Equals("2") ? MailPriority.High : MailPriority.Normal
            };

            // Attempt to send the email.
            var emailResult =  _emailService.SendEmail(mailMessageSettings, smtpServerSettings);
            if (!emailResult.Item1)
            {
                // Log the email was NOT sent
                _notify.ProcessingCompletion($"FAILED client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
            }

            //save email to email_settings table
var sentSaved = UpdateEmailSetting(mailMessageSettings, clientSettingsData.Code!, emailResult.Item1);
if (!sentSaved.Item1)
{
    // Log the email was NOT sent
    _notify.ProcessingCompletion($"FAILED to save sent email client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
    return;
            }

            // Log the email was sent
            _notify.ProcessingCompletion($"SUCCESS client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{sentSaved.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
        }
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

    private List<string> Recipients(int rptId)
    {
        try
        {
            var userEmails = _context.Contact
                .FromSql(@$"SELECT PrimaryEmail FROM Contact_Contact 
                                WHERE AppUser_Id IN (SELECT [UserId]FROM Security_UserRole
                                                        WHERE UserId IN (SELECT id FROM Security_User 
                                                                            WHERE active = 1 
                                                                            AND UserId IN (SELECT AppUser_Id FROM Contact_Contact 
                                                                                            WHERE AppUser_Id IS NOT NULL 
                                                                                            AND id IN (SELECT Contact_id FROM Contact_OrganizationAssociation 
                                                                                                        WHERE Id IN (SELECT Organization_Id FROM ReportingProfileTemplateOrganizations 
                                                                                                                        WHERE ReportingProfileTemplate_Id = {rptId} AND active = 1)))) 
                                                                            AND RoleId = (SELECT Id FROM Security_Role WHERE DisplayText = 'Submit Reporting'))")
                .Select(c => c.PrimaryEmail)
                .ToList();

            return userEmails;
        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
            return [];
        }
    }       
}
