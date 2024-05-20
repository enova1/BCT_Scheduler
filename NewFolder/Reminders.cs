using Microsoft.Extensions.Logging;
using NotificationService;

namespace BCT_Scheduler.NewFolder;

public class RemindersImpl(ILogger<Notify> log, string timeZone = "Eastern Standard Time") : BctReport(log, timeZone)
{
}