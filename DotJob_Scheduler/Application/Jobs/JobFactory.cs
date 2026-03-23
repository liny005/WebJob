using DotJob_Model.Entity;
using Quartz;
using Quartz.Spi;

namespace Job_Scheduler.Application.Jobs;

public class JobFactory : IJobFactory
{
    private readonly IServiceProvider _serviceProvider;

    public JobFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var jobType   = bundle.JobDetail.JobType;
        var logEntity = new LogEntity();
        var job = (IJob)Activator.CreateInstance(jobType, logEntity, _serviceProvider)!;
        return job;
    }

    public void ReturnJob(IJob job)
    {
        if (job is IDisposable disposable)
            disposable.Dispose();
    }
}
