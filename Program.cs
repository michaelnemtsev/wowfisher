namespace WowFisher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var instance = new Mutex(false, @"Local\WowFisher", out bool firstInstance);
        ApplicationConfiguration.Initialize();

        if (!firstInstance)
        {
            MessageBox.Show("The fishing overlay is already running in the system tray.", "WowFisher");
            return;
        }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WowFisher");
        using var application = new FishingApplication(folder);
        Application.Run(application);
    }
}
