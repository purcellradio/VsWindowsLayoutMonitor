namespace VsWindowsLayoutMonitor.Services.Settings
{
    public interface ISettingsServiceFactory
    {
        #region Public Methods

        ISettingsService Create();

        #endregion
    }
}