using S3Lite.Forms;
using S3Lite.Services;

namespace S3Lite;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Last-resort net: surface unexpected UI-thread exceptions instead of crashing
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, "Unexpected Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);

        var settings = SettingsStore.Load();
        Application.SetColorMode(settings.Theme == "Dark"
            ? SystemColorMode.Dark
            : SystemColorMode.Classic);
        Application.Run(new MainForm());
    }
}
