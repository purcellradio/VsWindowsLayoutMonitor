namespace VsWindowsLayoutMonitor.Services.Settings
{
    public class SettingsServiceFactory : ISettingsServiceFactory
    {
        #region Private Fields

        private readonly IServiceProvider serviceProvider;

        #endregion

        #region Constructors

        public SettingsServiceFactory(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        #endregion

        #region ISettingsServiceFactory Members

        public ISettingsService Create()
        {
            return serviceProvider.GetRequiredService<ISettingsService>();
        }

        #endregion
    }
}