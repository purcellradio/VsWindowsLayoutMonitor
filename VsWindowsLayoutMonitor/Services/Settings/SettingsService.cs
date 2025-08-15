using Microsoft.Extensions.Options;
using VsWindowsLayoutMonitor.Settings;

namespace VsWindowsLayoutMonitor.Services.Settings
{
    public class SettingsService : ISettingsService
    {
        #region Public Properties

        public double CarpsVersionNumber { get; }

        public ApplicationSettings ApplicationSettings { get; set; }

        #endregion

        #region Constructors

        public SettingsService(IOptionsMonitor<ApplicationSettings> optionsMonitor, ILogger<SettingsService> logger)
        {
            ApplicationSettings = optionsMonitor.CurrentValue;
        }

        #endregion
    }
}