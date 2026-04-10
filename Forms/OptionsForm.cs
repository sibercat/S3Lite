using S3Lite.Models;

namespace S3Lite.Forms;

public class OptionsForm : Form
{
    private NumericUpDown numUploads    = null!;
    private NumericUpDown numDownloads  = null!;
    private NumericUpDown numParts      = null!;
    private NumericUpDown numThreshold  = null!;
    private ComboBox      cmbTheme      = null!;
    private CheckBox      chkShowTray   = null!;
    private CheckBox      chkMinToTray  = null!;
    private CheckBox      chkPagination = null!;
    private NumericUpDown numPageSize   = null!;
    private CheckBox      chkPreviewLim = null!;
    private NumericUpDown numPreviewMB  = null!;

    public AppSettings Result { get; private set; }

    public OptionsForm(AppSettings current)
    {
        Result = current;
        InitializeComponent(current);
    }

    private void InitializeComponent(AppSettings current)
    {
        Text            = "S3 Lite Options";
        Size            = new Size(560, 650);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // ── Tab control ───────────────────────────────────────────────────────
        var tabs = new TabControl
        {
            Left   = 10, Top = 10,
            Width  = 522, Height = 560
        };

        tabs.TabPages.Add(BuildQueueingTab(current));
        tabs.TabPages.Add(BuildInterfaceTab(current));

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnSave = new Button
        {
            Text   = "✔ Save Changes",
            Width  = 130, Height = 30,
            Left   = 298, Top    = 580
        };
        btnSave.Click += (_, _) =>
        {
            Result = new AppSettings
            {
                MaxConcurrentUploads   = (int)numUploads.Value,
                MaxConcurrentDownloads = (int)numDownloads.Value,
                ParallelPartsPerUpload = (int)numParts.Value,
                MultipartThresholdMB   = (int)numThreshold.Value,
                Theme                  = cmbTheme.SelectedItem as string ?? "Light",
                ShowTrayIcon           = chkShowTray.Checked,
                MinimizeToTray         = chkMinToTray.Checked,
                EnablePagination       = chkPagination.Checked,
                PageSize               = (int)numPageSize.Value,
                LimitPreviewSize       = chkPreviewLim.Checked,
                PreviewMaxSizeMB       = (int)numPreviewMB.Value
            };
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button
        {
            Text  = "✖ Cancel",
            Width = 100, Height = 30,
            Left  = 440, Top    = 580
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange(new Control[] { tabs, btnSave, btnCancel });
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    // ── Queueing tab ──────────────────────────────────────────────────────────
    private TabPage BuildQueueingTab(AppSettings current)
    {
        var page = new TabPage("Queueing");

        var grp = new GroupBox
        {
            Text  = "Transfer limits",
            Left  = 8, Top = 8,
            Width = 494, Height = 310
        };

        grp.Controls.Add(MakeRow("Maximum number of concurrent uploads:",
            "Set the maximum number of upload tasks that can run simultaneously.",
            current.MaxConcurrentUploads, 1, 128, 16, out numUploads));

        grp.Controls.Add(MakeRow("Maximum number of concurrent downloads:",
            "Set the maximum number of download tasks that can run simultaneously.",
            current.MaxConcurrentDownloads, 1, 128, 88, out numDownloads));

        grp.Controls.Add(MakeRow("Parallel parts per multipart upload:",
            "Number of simultaneous part connections per large file. Higher = faster on fast connections.",
            current.ParallelPartsPerUpload, 1, 128, 160, out numParts));

        grp.Controls.Add(MakeRow("Multipart upload threshold (MB):",
            "Files at or above this size use multipart upload instead of a single request. Default: 16 MB.",
            current.MultipartThresholdMB, 5, 5120, 232, out numThreshold));

        var tip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 300, ReshowDelay = 200 };

        const string concurrentTip =
            "Recommended by connection speed:\n" +
            "  Slow      (< 10 Mbps)   →  1 – 2\n" +
            "  Average   (10–100 Mbps) →  3 – 5\n" +
            "  Fast      (100–500 Mbps)→  5 – 10\n" +
            "  Gigabit   (1 Gbps+)     →  10 – 32";

        const string partsTip =
            "Recommended by connection speed:\n" +
            "  Slow      (< 10 Mbps)   →  1 – 2\n" +
            "  Average   (10–100 Mbps) →  4 – 8\n" +
            "  Fast      (100–500 Mbps)→  8 – 16\n" +
            "  Gigabit   (1 Gbps+)     →  16 – 32\n\n" +
            "Only applies to files at or above the multipart threshold.";

        const string thresholdTip =
            "Files smaller than this are uploaded in a single request.\n" +
            "Files at or above this size are split into parts and uploaded in parallel.\n\n" +
            "Recommended values:\n" +
            "  Slow connection  (< 10 Mbps)    →  64 – 128 MB\n" +
            "  Average          (10–100 Mbps)  →  16 – 64 MB\n" +
            "  Fast / Gigabit   (100 Mbps+)    →  8 – 16 MB\n\n" +
            "Lower threshold = more files benefit from parallel parts.\n" +
            "Minimum: 5 MB (S3 requirement for multipart parts).";

        tip.SetToolTip(numUploads,   concurrentTip);
        tip.SetToolTip(numDownloads, concurrentTip);
        tip.SetToolTip(numParts,     partsTip);
        tip.SetToolTip(numThreshold, thresholdTip);

        page.Controls.Add(grp);
        return page;
    }

    // ── Interface tab ─────────────────────────────────────────────────────────
    private TabPage BuildInterfaceTab(AppSettings current)
    {
        var page = new TabPage("Interface");

        // ── System tray ───────────────────────────────────────────────────────
        var grpTray = new GroupBox
        {
            Text  = "System tray",
            Left  = 8, Top = 8,
            Width = 494, Height = 116
        };

        chkShowTray = new CheckBox
        {
            Text    = "Always show S3 Lite icon in system tray",
            Left    = 10, Top = 22,
            Width   = 460, Height = 20,
            Checked = current.ShowTrayIcon
        };
        grpTray.Controls.Add(new Label
        {
            Text      = "This setting toggles whether S3 Lite has an icon in the system tray.",
            Left      = 10, Top = 44,
            Width     = 470, Height = 16,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f)
        });

        chkMinToTray = new CheckBox
        {
            Text    = "Minimize to system tray",
            Left    = 10, Top = 62,
            Width   = 460, Height = 20,
            Checked = current.MinimizeToTray
        };
        grpTray.Controls.Add(new Label
        {
            Text      = "Hide S3 Lite to the system tray when minimized.",
            Left      = 10, Top = 84,
            Width     = 470, Height = 16,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f)
        });

        grpTray.Controls.AddRange(new Control[] { chkShowTray, chkMinToTray });

        // Enabling MinimizeToTray requires ShowTrayIcon
        chkMinToTray.Enabled = chkShowTray.Checked;
        chkShowTray.CheckedChanged += (_, _) =>
        {
            chkMinToTray.Enabled = chkShowTray.Checked;
            if (!chkShowTray.Checked) chkMinToTray.Checked = false;
        };

        // ── Color theme ───────────────────────────────────────────────────────
        var grpTheme = new GroupBox
        {
            Text  = "Color theme",
            Left  = 8, Top = 132,
            Width = 494, Height = 80
        };

        cmbTheme = new ComboBox
        {
            Left          = 10, Top = 22,
            Width         = 200,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbTheme.Items.AddRange(new object[] { "Light", "Dark" });
        cmbTheme.SelectedItem = current.Theme == "Dark" ? "Dark" : "Light";

        grpTheme.Controls.Add(cmbTheme);
        grpTheme.Controls.Add(new Label
        {
            Text      = "Changing the theme requires an application restart to take effect.",
            Left      = 10, Top = 52,
            Width     = 470, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f)
        });

        // ── Performance optimization ──────────────────────────────────────────
        var grpPerf = new GroupBox
        {
            Text  = "Performance optimization",
            Left  = 8, Top = 220,
            Width = 494, Height = 150
        };

        chkPagination = new CheckBox { Text = "Enable bucket pagination with page size:", Left = 10, Top = 20, Width = 460, Height = 20, Checked = current.EnablePagination };
        numPageSize   = new NumericUpDown { Left = 10, Top = 42, Width = 466, Minimum = 100, Maximum = 1000, Value = Math.Clamp(current.PageSize, 100, 1000) };
        numPageSize.Enabled = current.EnablePagination;
        grpPerf.Controls.Add(new Label { Text = "S3 Lite will paginate bucket listings based on the specified page size (max 1000).", Left = 10, Top = 66, Width = 466, Height = 16, ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f) });
        chkPagination.CheckedChanged += (_, _) => numPageSize.Enabled = chkPagination.Checked;

        chkPreviewLim = new CheckBox { Text = "Limit the max file size for Preview (MB):", Left = 10, Top = 88, Width = 460, Height = 20, Checked = current.LimitPreviewSize };
        numPreviewMB  = new NumericUpDown { Left = 10, Top = 110, Width = 466, Minimum = 1, Maximum = 500, Value = Math.Clamp(current.PreviewMaxSizeMB, 1, 500) };
        numPreviewMB.Enabled = current.LimitPreviewSize;
        grpPerf.Controls.Add(new Label { Text = "Set a maximum file size limit for downloading files for preview.", Left = 10, Top = 130, Width = 466, Height = 16, ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f) });
        chkPreviewLim.CheckedChanged += (_, _) => numPreviewMB.Enabled = chkPreviewLim.Checked;

        grpPerf.Controls.AddRange(new Control[] { chkPagination, numPageSize, chkPreviewLim, numPreviewMB });

        page.Controls.AddRange(new Control[] { grpTray, grpTheme, grpPerf });
        return page;
    }

    // ── Helper ────────────────────────────────────────────────────────────────
    private static Panel MakeRow(string label, string hint, int value, int min, int max, int top, out NumericUpDown spinner)
    {
        var pnl = new Panel { Left = 8, Top = top, Width = 470, Height = 66 };

        pnl.Controls.Add(new Label { Text = label, Left = 0, Top = 0, Width = 450, Height = 18 });

        var num = new NumericUpDown
        {
            Left    = 0, Top  = 20,
            Width   = 454,
            Minimum = min, Maximum = max,
            Value   = Math.Clamp(value, min, max)
        };
        pnl.Controls.Add(num);

        pnl.Controls.Add(new Label
        {
            Text      = hint,
            Left      = 0, Top = 46,
            Width     = 454, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f)
        });

        spinner = num;
        return pnl;
    }
}
