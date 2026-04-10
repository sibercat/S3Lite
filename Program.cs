using S3Lite.Forms;
using S3Lite.Services;

namespace S3Lite;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        var settings = SettingsStore.Load();
        Application.SetColorMode(settings.Theme == "Dark"
            ? SystemColorMode.Dark
            : SystemColorMode.Classic);
        Application.Run(new MainForm());
    }
}