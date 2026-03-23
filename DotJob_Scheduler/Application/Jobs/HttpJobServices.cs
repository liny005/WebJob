using Quartz;

namespace Job_Scheduler.Application.SchedulerCenter;

public class HttpJobServices:IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        throw new NotImplementedException();
    }
}