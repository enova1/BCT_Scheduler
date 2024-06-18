using Microsoft.Extensions.Logging;

namespace BCT_Scheduler.NewFolder;

public class RemindersImpl(ILogger<BctReport> log, string timeZone = "Eastern Standard Time") : BctReport(log,  timeZone)
{
}


public class ContractsImpl(ILogger<BctContracts> log, string timeZone = "Eastern Standard Time") : BctContracts(log, timeZone)
{
}