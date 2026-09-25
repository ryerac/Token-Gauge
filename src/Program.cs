namespace TokenGauge;

internal static class Program
{
    const string ShowSettingsEvent = @"Local\TokenGauge.ShowSettings";

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\TokenGauge", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Already running: launching it again opens the settings window in the running copy.
            if (EventWaitHandle.TryOpenExisting(ShowSettingsEvent, out var showSettings))
            {
                Native.AllowSetForegroundWindow(Native.ASFW_ANY);
                showSettings.Set();
                showSettings.Dispose();
            }
            return;
        }

        using var showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEvent);
        ApplicationConfiguration.Initialize();
        Application.Run(new GaugeContext(showSettingsSignal));
    }
}
