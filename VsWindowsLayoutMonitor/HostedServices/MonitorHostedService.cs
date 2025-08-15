using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Quartz;

namespace VsWindowsLayoutMonitor.HostedServices
{
    internal class MonitorHostedService : BackgroundService
    {
        #region Private Fields

        private readonly ILogger<MonitorHostedService> logger;
        private readonly ISchedulerFactory schedulerFactory;

        #endregion

        #region Constructors

        public MonitorHostedService(ILogger<MonitorHostedService> logger, ISchedulerFactory schedulerFactory)
        {
            this.logger = logger;
            this.schedulerFactory = schedulerFactory;
        }

        #endregion

        #region Protected Methods

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // For a Quartz Job that runs to a schedule

                var scheduler = await schedulerFactory.GetScheduler(stoppingToken);

                var job = JobBuilder.Create<Jobs.MonitorJob>()
                    .WithIdentity("MonitorJob", "Samples")
                    .Build();

                var trigger = TriggerBuilder.Create()
                    .WithIdentity("MonitorJobTrigger", "Samples")
                    .StartNow()
                    .WithSimpleSchedule(x => x
                        .WithInterval(TimeSpan.FromSeconds(10))
                        .RepeatForever())
                    .Build();

                await scheduler.ScheduleJob(job, trigger, stoppingToken);

                logger.LogInformation("MonitorHostedService scheduled MonitorJob.");

                // Keep the service running until cancellation is requested
                while (!stoppingToken.IsCancellationRequested)
                {
                    // If you want to do something periodically in the background service
                    // you can do it here.

                    // Example of doing something periodically including timestamping:

                    //logger.LogInformation("MonitorHostedService is doing something at: {time}", DateTimeOffset.Now);

                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
            catch (TaskCanceledException)
            {
                logger.LogInformation("MonitorHostedService task was canceled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An unexpected error occurred in MonitorHostedService.ExecuteAsync.");
            }
        }

        #endregion
    }
}