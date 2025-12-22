namespace MudoSoft.Tray;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Ensure single instance
        using var mutex = new Mutex(true, "MudoSoftTrayMutex", out bool createdNew);
        if (!createdNew)
        {
            // Already running
            return;
        }

        ApplicationConfiguration.Initialize();
        
        // Hide default form, run as tray application
        Application.Run(new TrayApplicationContext());
    }
}