using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using DataAccess;
using DataAccess.BctModels;
using Microsoft.Extensions.Logging;
using NotificationService;
using Smtp;
using Smtp.Models;

namespace BCT_Scheduler;

/// <summary>
/// Custom Report Reminder class for sending email reminders.
/// </summary>
/// <param name="timeZone"></param>
public class BctReport(ILogger<Notify> log, BctDbContext context, string timeZone = "Eastern Standard Time")
{
    private readonly EmailService _emailService = new ();
    private readonly Notify _notify = new (log);
    private readonly DateTime _estDateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone));

    public void SendReminderEmail(int rptTemplateId)
    {
        //Get the report reminders.
        var rptReminderData = context.ReportReminders.Where(rt => rt.ReportId == rptTemplateId && rt.Active == true);
        if (rptReminderData == null)
            throw new Exception("Report Template not found.");

        //Get the report template settings.
        var rptTemplateSettingsData = context.ReportingProfileTemplates.FirstOrDefault(rts => rts.Id == rptTemplateId && rts.Active == true);
        if (rptTemplateSettingsData == null)
            throw new Exception("Report Template Settings not found.");

        //Start processing the report reminders.
        foreach (ReportReminder reminder in rptReminderData)
        {
            var nod = reminder.NumberOfDays;
            var wts = reminder.WhenToSend;
            var months = reminder.Months;
            var freq = rptTemplateSettingsData.ReportingFrequency;
            
        }



        //Get the client settings.
        var clientSettingsData = context.AdminClients.FirstOrDefault(ac => ac.Id == rptTemplateSettingsData.Client_Id && ac.Active == true);
        if (clientSettingsData == null)
            throw new Exception("Client Settings not found.");


        //Get the email settings.
        var emailSettings = context.EmailSettings.FirstOrDefault(es => es.TenantCode == clientSettingsData.Code && es.Active == true);
        if (emailSettings == null) 
            throw new Exception("Email settings not found.");

        // Create SMTP server settings object.
        SmtpServerSettings smtpServerSettings = new()
        {
            SmtpServer = emailSettings.SmtpServer,
            Port = emailSettings.Port,
            SenderEmail = emailSettings.Sender,
            SenderPassword = emailSettings.Password
        };

        var adminClientSettings = context.AdminClientSettings.FirstOrDefault(es => es.ClientCode == "TX");

        if (adminClientSettings == null)
            throw new Exception("AdminClientSettings settings not found.");

        // Create the EMAIL message settings object.
        MailMessageSettings mailMessageSettings = new()
        {
// PICK UP HERE IN THE MORNING. BY FIGURING OUT WHAT DATA TO PULL.

            To = "Chris.tate@b2Gnow.com",
            From = adminClientSettings.SupportEmail,
            Sender = emailSettings.Sender,
            Subject = "THIS IS A TEST",
            Body = $"HELLO YAL:L: {rptTemplateId}",
            IsBodyHtml = true,
            Priority = MailPriority.Normal
        };

        // Attempt to send the email.
        var emailResult = _emailService.SendEmail(mailMessageSettings, smtpServerSettings);
        if (!emailResult.Item1)
        {
            // Log the email was NOT sent
            _notify.ProcessingCompletion($"FAILED clientId:name:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
        }

        // Do success sent email logic.
    }
}