using S3Lite.Models;

namespace S3Lite.Forms;

public class CompressionRuleEditForm : Form
{
    private TextBox        txtBucket  = null!;
    private TextBox        txtFile    = null!;
    private TrackBar       trkLevel   = null!;
    private Label          lblLevel   = null!;
    private CheckBox       chkEnabled = null!;

    public CompressionRule Result { get; private set; }

    public CompressionRuleEditForm(CompressionRule? existing = null)
    {
        Result = existing != null
            ? new CompressionRule { BucketMask = existing.BucketMask, FileMask = existing.FileMask, Level = existing.Level, Enabled = existing.Enabled }
            : new CompressionRule();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        bool isEdit     = Result.BucketMask != "*" || Result.FileMask != "*" || Result.Level != 6;
        Text            = isEdit ? "Edit Compression Rule" : "Add Compression Rule";
        Size            = new Size(500, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false; MinimizeBox = false;

        int y = 16;

        // ── Bucket mask ───────────────────────────────────────────────────────
        Controls.Add(MakeLabel("Bucket name or mask:", 12, y));
        y += 20;
        txtBucket = new TextBox { Left = 12, Top = y, Width = 458, Text = Result.BucketMask };
        Controls.Add(txtBucket);
        Controls.Add(MakeHint("Use * to match all buckets, or enter a bucket name. Wildcards: * = any, ? = one char.", 12, y + 24));
        y += 56;

        // ── File mask ─────────────────────────────────────────────────────────
        Controls.Add(MakeLabel("File name or mask:", 12, y));
        y += 20;
        txtFile = new TextBox { Left = 12, Top = y, Width = 458, Text = Result.FileMask };
        Controls.Add(txtFile);
        Controls.Add(MakeHint("Examples: * (all files), *.html, *.css, *.js, assets/*, docs/*.txt", 12, y + 24));
        y += 56;

        // ── Compression level ────────────────────────────────────────────────
        Controls.Add(MakeLabel("GZip compression level:", 12, y));
        y += 20;

        var pnlSlider = new Panel { Left = 12, Top = y, Width = 458, Height = 58 };

        trkLevel = new TrackBar
        {
            Left = 0, Top = 0, Width = 390, Height = 40,
            Minimum = 1, Maximum = 9,
            Value   = Math.Clamp(Result.Level, 1, 9),
            TickFrequency = 1, SmallChange = 1, LargeChange = 1,
        };
        lblLevel = new Label
        {
            Left = 396, Top = 10, Width = 30, Height = 20,
            Text = trkLevel.Value.ToString(),
            Font = new Font(Font.FontFamily, 10f, FontStyle.Bold)
        };
        trkLevel.ValueChanged += (_, _) => lblLevel.Text = trkLevel.Value.ToString();

        var lblFast = new Label { Left = 0,   Top = 42, Width = 80,  Height = 16, Text = "best speed",        ForeColor = SystemColors.GrayText, Font = new Font(Font.FontFamily, 7.5f) };
        var lblBest = new Label { Left = 300, Top = 42, Width = 90,  Height = 16, Text = "best compression",  ForeColor = SystemColors.GrayText, Font = new Font(Font.FontFamily, 7.5f), TextAlign = ContentAlignment.MiddleRight };

        pnlSlider.Controls.AddRange(new Control[] { trkLevel, lblLevel, lblFast, lblBest });
        Controls.Add(pnlSlider);
        y += 76;

        Controls.Add(MakeHint("Level 1 = fastest, least compression. Level 9 = slowest, best compression. Default: 6", 12, y));
        y += 28;

        // ── Enabled ──────────────────────────────────────────────────────────
        chkEnabled = new CheckBox { Left = 12, Top = y, Width = 300, Height = 22, Text = "Rule is enabled", Checked = Result.Enabled };
        Controls.Add(chkEnabled);
        y += 36;

        // ── Buttons ──────────────────────────────────────────────────────────
        var btnOk = new Button { Text = isEdit ? "✔ Save Changes" : "✔ Add Rule", Width = 130, Height = 30, Left = 228, Top = y };
        var btnCancel = new Button { Text = "✖ Cancel", Width = 100, Height = 30, Left = 370, Top = y, DialogResult = DialogResult.Cancel };
        btnOk.Click += (_, _) =>
        {
            Result.BucketMask = string.IsNullOrWhiteSpace(txtBucket.Text) ? "*" : txtBucket.Text.Trim();
            Result.FileMask   = string.IsNullOrWhiteSpace(txtFile.Text)   ? "*" : txtFile.Text.Trim();
            Result.Level      = trkLevel.Value;
            Result.Enabled    = chkEnabled.Checked;
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ClientSize = new Size(482, y + 44);
    }

    private static Label MakeLabel(string text, int x, int y) =>
        new() { Text = text, Left = x, Top = y, Width = 450, Height = 18, Font = new Font(SystemFonts.DefaultFont, FontStyle.Regular) };

    private static Label MakeHint(string text, int x, int y) =>
        new() { Text = text, Left = x, Top = y, Width = 458, Height = 16, ForeColor = SystemColors.GrayText, Font = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f) };
}
