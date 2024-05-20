using System.Net.Mail;
using DataAccess;
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

    public void SendReminderEmail(int clientId, string name, int rptTemplateId)
    {
        //TODO: Goto database to get the SMTP and Email settings.
        var emailSettings = context.EmailSettings.FirstOrDefault(es => es.TenantCode == "TX");
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
-- PICK UP HERE IN THE MORNING. BY FIGURING OUT WHAT DATA TO PULL.

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
            _notify.ProcessingCompletion($"FAILED {clientId}:{name}:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
        }

        // Do success sent email logic.
    }
}