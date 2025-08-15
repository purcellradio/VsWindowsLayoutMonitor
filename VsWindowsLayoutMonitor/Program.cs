using CliWrap;
using Quartz;
using Serilog;
using VsWindowsLayoutMonitor.HostedServices;
using VsWindowsLayoutMonitor.Jobs;
using VsWindowsLayoutMonitor.Services.Settings;
using VsWindowsLayoutMonitor.Settings;

// REFERENCES:
// Create Windows Service using BackgroundService: https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service
// Create a Windows Service installer: https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service-with-installer
//
// LIBRARIES:
// NLog: https://github.com/NLog/NLog/wiki/Getting-started-with-ASP.NET-Core-6
// CliWrap: https://github.com/Tyrrrz/CliWrap

// Configure a bootstrap logger
Log.Logger = new LoggerConfiguration().WriteTo.Console()
    .WriteTo.Debug()
    .CreateBootstrapLogger();

// Service variables

const string serviceName = "0-VsWindowsLayoutMonitor";

var executablePath = Path.Combine(
    AppContext.BaseDirectory,
    System.Diagnostics.Process.GetCurrentProcess()
        .MainModule?.FileName ?? ""
);

Log.Debug($"serviceName={serviceName}");
Log.Debug($"binPath={executablePath}");

// Required event arguments for install and uninstalling service via a Setup Project (optional)

if (args is { Length: 1 })
{
    if (args[0] is "/Install")
    {
        await Cli.Wrap("sc")
            .WithArguments(
                new[]
                {
                    "create",
                    serviceName,
                    $"binPath={executablePath}",
                    "start=auto"
                }
            )
            .ExecuteAsync();
    }
    else if (args[0] is "/Uninstall")
    {
        // WARNING: if an unhandled error is thrown by cli app during uninstall
        // setup will stop un-installation and throw its own error.
        //
        // Using SC.EXE:
        // .WithValidation to none suppresses process exit codes greater than 0 causing exception
        // typically thrown when ERROR_INVALID_SERVICE_CONTROL, ERROR_SERVICE_NOT_ACTIVE, ERROR_ACCESS_DENIED
        // in situations where changes to the service were made manually
        //
        // Other uncaught errors:
        // Log these errors or view the event log as they will not be reported during uninstall 
        try
        {
            await Cli.Wrap("sc")
                .WithArguments(
                    new[]
                    {
                        "stop",
                        serviceName
                    }
                )
                .WithValidation(CommandResultValidation.None) // prevents cli validation errors (optional)
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            // with validation on, reports the cli error
            Log.Error($"Error stopping service during uninstall");
            Log.Error($"{ex.Message}");
        }

        try
        {
            await Cli.Wrap("sc")
                .WithArguments(
                    new[]
                    {
                        "delete",
                        serviceName
                    }
                )
                .WithValidation(CommandResultValidation.None) // prevents cli validation errors (optional)
                .ExecuteAsync();
        }
        catch (Exception ex)
        {
            // with validation on, reports the cli error
            Log.Error($"Error uninstalling service");
            Log.Error($"{ex.Message}");
        }
    }

    return;
}

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
        {
            options.ServiceName = serviceName;
        }
    )
    .UseSerilog((context, services, configuration) => configuration.ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
    )
    .ConfigureServices((context, services) =>
        {
            // Configure ApplicationSettings using the options pattern
            services.Configure<ApplicationSettings>(context.Configuration.GetSection("ApplicationSettings"));

            // Register settings services
            services.AddSingleton<ISettingsService, SettingsService>()
                .AddSingleton<ISettingsServiceFactory, SettingsServiceFactory>();

            // DI for classes
            services.AddHostedService<MonitorHostedService>();

            // Add Quartz services
            services.AddQuartz();
            services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

            services.AddTransient<MonitorJob>();
        }
    )
    .Build();

// when running as console application, stop service with ESC key
_ = Task.Run(async () =>
    {
        ConsoleKeyInfo key;

        do
        {
            key = Console.ReadKey();
        }
        while (key.Key != ConsoleKey.Escape);

        await host.StopAsync();
    }
);

Log.Debug("Starting service...");

await host.RunAsync();