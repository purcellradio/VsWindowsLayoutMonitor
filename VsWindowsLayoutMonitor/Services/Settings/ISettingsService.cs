using VsWindowsLayoutMonitor.Settings;

namespace VsWindowsLayoutMonitor.Services.Settings
{
    public interface ISettingsService
    {
        #region Public Properties

        ApplicationSettings ApplicationSettings { get; set; }

        #endregion
    }
}