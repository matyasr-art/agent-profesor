using Velopack;

namespace AgentProfesor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Musí běžet jako úplně první věc v Main – zpracovává Velopack
        // instalační/aktualizační příkazy (--veloapp-install apod.), než
        // se appka rozjede jako tray ikona.
        VelopackApp.Build().Run();

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayContext());
    }
}
