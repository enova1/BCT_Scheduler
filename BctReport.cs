using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService;

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
    private readonly Common _commonSendEmail = new ();
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
                .Replace("[Reporting_Display_Name]", rptTemplateSettingsData.DisplayName)
                .Replace("[numberofdays]", rptReminderData.NumberOfDays.ToString());
            var emailSubject = emailTemplate.Subject!
                .Replace("[Reporting_Display_Name]", rptTemplateSettingsData.DisplayName);

            // Get the recipients.
            var recipients = Recipients(rptTemplateSettingsData.Id);
            var recipientsString = string.Join(",", recipients);

            // Attempt to send the email.
            var emailResult =  _commonSendEmail.SendEmail(clientSettingsData.Code!, recipientsString,emailSubject,emailBody, rptReminderData.WhenToSend);
            if (!emailResult.Item1)
            {
                // Log the email was NOT sent
                _notify.ProcessingCompletion($"FAILED client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}) / result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
            }


            // Log the email was sent
            _notify.ProcessingCompletion($"SUCCESS client:{clientSettingsData.Code} / reminder:{rptTemplateSettingsData.DisplayName}({beforeAfter}-{month}):{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
        }
    }

    /// <summary>
    /// Get Primary Email Recipients for the report template.
    /// </summary>
    /// <param name="rptId"></param>
    /// <returns></returns>
    private List<string> Recipients(int rptId)
    {
        try
        {
            var userEmails = _context.Contacts
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
