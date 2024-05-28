using DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NotificationService;

namespace BCT_Scheduler.NewFolder;

public class RemindersImpl(ILogger<BctReport> log, string timeZone = "Eastern Standard Time") : BctReport(log,  timeZone)
{
}