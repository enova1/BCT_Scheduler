
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService;
using DataAccess.BctModels;

namespace BCT_Scheduler;


public class BctContracts(ILogger<BctContracts> log, string timeZone = "Eastern Standard Time")
{
    private readonly Common _commonSendEmail = new();
    private readonly Notify _notify = new();
    private readonly DateTime _estDateTime = TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(timeZone));
    private readonly BctDbContext _context = new(new DbContextOptions<BctDbContext>());


    /// <summary>
    /// Check if the contract is expired and send a reminder email
    /// </summary>
    /// <returns></returns>
    public void CheckExpiration (int days)
    {
        try
        {
            var result30 = ProcessExpiredContracts(GetExpiredContracts(days));
            if (!result30.Item1)
            {
                // Log the email was NOT sent
                _notify.ProcessingCompletion($"DONE result:{result30.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
            }

            _notify.ProcessingCompletion($"SUCCESS {_estDateTime:MM/dd/yyyy hh:mm:ss tt}");

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

    }

    private (bool, string) ProcessExpiredContracts(List<Contract> contracts, bool is180 = false)
    {
        var emailsSent = 0;
        if (contracts.Count <= 0) return (false, "No Contracts to process.");

        foreach (var contract in contracts)
        {
            var program = _context.FundSourceTypes
                .Where(p => p.Id == contract.FundSourceType_Id && p.Active)
                .Select(p => new { p.Name, p.CommonName, p.Type })
                .FirstOrDefault() ?? throw new Exception($"Contract Program was not found.");

            var organization = _context.Organizations
                .Where(o => o.Id == contract.Organization_Id)
                .Select(o => new { o.CommonName, o.LegalName })
                .FirstOrDefault() ?? throw new Exception($"Organization was not found.");

            var clientCode = _context.Clients
                .Where(ac => ac.Id == contract.Client_Id && ac.Active == true)
                .Select(ac => new { ac.Code })
                .FirstOrDefault() ?? throw new Exception($"Client Code not found for {contract.Client_Id}.");

            var contractName = _context.ContractTypes
                .Where(c => c.Id == contract.ContractType_Id)
                .Select(c => new { c.Name })
                .FirstOrDefault() ?? throw new Exception($"Contract Type was not found.");

            // Get the domain based on the environment
            var domain = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") switch
            {
                "Development" => "localhost",
                "QA" => $"Test-{clientCode.Code}dot.blackcattransit.com",
                "Staging" => $"staging-{clientCode.Code}dot.blackcattransit.com",
                "Production" => $"{clientCode.Code}dot.blackcattransit.com",
                _ => "localhost"
            };

            var link = $"https://{domain}/organization/{contract.Organization_Id}/contract/{contract.Id}?pid=0";


            var emailNotificationType = _context.EmailNotificationTypes
                .Where(ent => ent.NotificationType == "ContractExpiration" && ent.ClientCode == clientCode.Code && ent.Active)
                .Select(ent => new { ent.Id })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {contract.Id}");

            var emailTemplate = _context.EmailTemplates
                .Where(et => Equals(et.EmailType_Id, emailNotificationType.Id))
                .Select(et => new { et.Template, et.Subject })
                .FirstOrDefault() ?? throw new Exception($"Email Template Settings were not found. {contract.Id}");

            // Replace the email template & Subject [data] with the actual data.
            var emailBody = emailTemplate.Template!
                .Replace("[days]", is180 ? "180" : "30")
                .Replace("[organization]", $"{organization.LegalName} ({organization.CommonName})")
                .Replace("[program]", $"{program.CommonName}")
                .Replace("[year]", $"{contract.ContractYear}")
                .Replace("[contract]", $"{contractName.Name}({contract.Id})")
                .Replace("[expiration]", $"{contract.ExpirationDate}")
                .Replace("[requestlink]", $"<a href='{link}'>View Contracts Permissions</a>");

            var emailSubject = emailTemplate.Subject!;

            var recipients = Recipients(contract.Organization_Id);
            var recipientsString = string.Join(",", recipients);

            if (recipients.Count <= 0) continue;

            emailsSent += recipients.Count;

            // Send email to recipients
            var emailResult = _commonSendEmail.SendEmail(clientCode.Code!, recipientsString, emailSubject, emailBody, "2");
            if (!emailResult.Item1)
            {
                return (false, $"FAILED result:{emailResult.Item2}:{_estDateTime:MM/dd/yyyy hh:mm:ss tt}");
                        
            }
        }
        return (true, $"{emailsSent} Emails sent successfully");
    }

    /// <summary>
    /// Get a list of Recipients where the contract has expired in the next 30 or 180 days
    /// </summary>
    /// <param name="orgId"></param>
    /// <returns></returns>
    private List<string> Recipients(int orgId)
    {
        try
        {
            var userEmails = _context.Contacts
                .FromSql(@$"select PrimaryEmail from Contact_Contact 
                            where AppUser_Id in (SELECT [UserId] FROM [dbo].[Security_UserRole] 
					                             WHERE UserId IN (select id from Security_User 
					                                              where active = 1 and UserId IN (select AppUser_Id from Contact_Contact 
									                                                               where AppUser_Id is not null AND id in (select Contact_id from Contact_OrganizationAssociation 
																	                                                                        where Organization_Id in ({orgId}))))  
                           AND RoleId = (SELECT Id FROM Security_Role where DisplayText = 'View Contracts'))")
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

    /// <summary>
    /// Get a list of contracts that are expired in the next 30 or 180 days
    /// </summary>
    /// <param name="days"></param>
    /// <returns></returns>
    private List<Contract> GetExpiredContracts(int days)
    {
        try
        {
            var contracts = _context.Contracts
            .FromSql(@$"SELECT Id, Organization_Id, Client_Id, ExpirationDate, ContractYear, FundSourceType_Id, isNull(ContractType_Id,1) AS [ContractType_Id]
                            FROM Project_Contract 
                            WHERE status not in ('Pending Contract', 'Pending Amendment', 'Completed') 
                                  AND DATEADD(day, {days}, ExpirationDate) = CAST(GETDATE() AS DATE)")
            .ToList();

        return contracts;
        }
        catch (Exception e)
        {
            log.LogError(e, e.Message);
            return [];
        }
    }
}