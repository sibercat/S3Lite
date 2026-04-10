using S3Lite.Models;
using S3Lite.Services;

namespace S3Lite.Forms;

public class WebUrlForm : Form
{
    private readonly S3Service _s3;
    private readonly string _bucket;
    private readonly List<S3Item> _items;

    private CheckBox chkHttps     = null!;
    private ComboBox cboExpiry    = null!;
    private NumericUpDown numMinutes = null!;
    private DateTimePicker dtpExpiry = null!;
    private GroupBox grpExpiry    = null!;

    private ComboBox cboHostname  = null!;
    private TextBox txtHostname   = null!;
    private GroupBox grpHostname  = null!;

    private TextBox txtUrl        = null!;
    private Button btnCopy        = null!;

    public WebUrlForm(S3Service s3, string bucket, List<S3Item> items)
    {
        _s3     = s3;
        _bucket = bucket;
        _items  = items;
        InitializeComponent();
        UpdateExpiryControls();
        UpdateHostnameControls();
        RegenerateUrls();
    }

    private void InitializeComponent()
    {
        Text            = "Web URL Generator";
        Size            = new Size(700, 490);
        MinimumSize     = new Size(540, 430);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;

        // ── HTTPS checkbox ────────────────────────────────────────────────────
        chkHttps = new CheckBox
        {
            Text    = "Use secure transfer (HTTPS)",
            Left    = 12, Top = 10,
            Width   = 300, Height = 20,
            Checked = true,
            Font    = new Font(Font.FontFamily, 9f, FontStyle.Regular)
        };
        var lblHttpsHint = new Label
        {
            Text      = "Check this box if your files contain sensitive information.",
            Left      = 30, Top = 30,
            Width     = 500, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 8f)
        };
        chkHttps.CheckedChanged += (_, _) => { UpdateHostnameControls(); RegenerateUrls(); };

        // ── Expiration ────────────────────────────────────────────────────────
        grpExpiry = new GroupBox
        {
            Text   = "Expiration",
            Left   = 8, Top = 52,
            Width  = 666, Height = 54,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        cboExpiry = new ComboBox
        {
            Left          = 8, Top = 18,
            Width         = 642,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor        = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        cboExpiry.Items.AddRange(new object[]
        {
            "URL should not expire",
            "URL should expire after defined number of minutes",
            "URL should expire on exact date"
        });
        cboExpiry.SelectedIndex = 0;
        cboExpiry.SelectedIndexChanged += (_, _) => { UpdateExpiryControls(); RegenerateUrls(); };

        numMinutes = new NumericUpDown
        {
            Left    = 8, Top  = 46,
            Width   = 180,
            Minimum = 1, Maximum = 99999, Value = 60,
            Visible = false
        };
        numMinutes.ValueChanged += (_, _) => RegenerateUrls();

        dtpExpiry = new DateTimePicker
        {
            Left         = 8, Top = 46,
            Width        = 280,
            Format       = DateTimePickerFormat.Custom,
            CustomFormat = "MMMM dd, yyyy  HH:mm:ss",
            Value        = DateTime.Now.AddDays(1),
            Visible      = false
        };
        dtpExpiry.ValueChanged += (_, _) => RegenerateUrls();

        grpExpiry.Controls.AddRange(new Control[] { cboExpiry, numMinutes, dtpExpiry });

        // ── Hostname ──────────────────────────────────────────────────────────
        grpHostname = new GroupBox
        {
            Text   = "Hostname",
            Left   = 8, Top = 70,
            Width  = 666, Height = 72,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        cboHostname = new ComboBox
        {
            Left          = 8, Top = 18,
            Width         = 642,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor        = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        cboHostname.Items.AddRange(new object[]
        {
            "Use default host name",
            "Use custom host name",
            "Use bucket name as host name"
        });
        cboHostname.SelectedIndex = 0;
        cboHostname.SelectedIndexChanged += (_, _) => { UpdateHostnameControls(); RegenerateUrls(); };

        txtHostname = new TextBox
        {
            Left      = 8, Top = 44,
            Width     = 642,
            ReadOnly  = true,
            BackColor = SystemColors.ControlLight,
            Anchor    = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        txtHostname.TextChanged += (_, _) => RegenerateUrls();

        grpHostname.Controls.AddRange(new Control[] { cboHostname, txtHostname });

        // ── URL output ────────────────────────────────────────────────────────
        txtUrl = new TextBox
        {
            Left       = 8,   Top      = 150,
            Width      = 666, Height   = 220,
            Multiline  = true,
            ReadOnly   = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor  = SystemColors.Window,
            Font       = new Font("Consolas", 8.5f),
            Anchor     = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
        };

        // ── Buttons ───────────────────────────────────────────────────────────
        btnCopy = new Button { Text = "Copy", Width = 90, Height = 28 };
        btnCopy.Click += BtnCopy_Click;

        var btnClose = new Button { Text = "Close", Width = 90, Height = 28, DialogResult = DialogResult.Cancel };

        var pnlButtons = new FlowLayoutPanel
        {
            Dock          = DockStyle.Bottom,
            Height        = 38,
            FlowDirection = FlowDirection.RightToLeft,
            Padding       = new Padding(4, 4, 4, 0)
        };
        pnlButtons.Controls.AddRange(new Control[] { btnClose, btnCopy });

        Controls.AddRange(new Control[] { chkHttps, lblHttpsHint, grpExpiry, grpHostname, txtUrl, pnlButtons });
        CancelButton = btnClose;

        Resize += (_, _) => RelayoutDynamic();
    }

    // ── Dynamic layout ────────────────────────────────────────────────────────
    private void UpdateExpiryControls()
    {
        int sel = cboExpiry.SelectedIndex;
        numMinutes.Visible = sel == 1;
        dtpExpiry.Visible  = sel == 2;
        grpExpiry.Height   = sel == 0 ? 54 : 80;
        RelayoutDynamic();
    }

    private void UpdateHostnameControls()
    {
        int sel = cboHostname.SelectedIndex;
        switch (sel)
        {
            case 0: // default
                txtHostname.ReadOnly  = true;
                txtHostname.BackColor = SystemColors.ControlLight;
                txtHostname.Text      = _s3.DefaultHostname(_bucket, chkHttps.Checked);
                break;
            case 1: // custom
                txtHostname.ReadOnly  = false;
                txtHostname.BackColor = SystemColors.Window;
                txtHostname.Text      = "";
                txtHostname.Focus();
                break;
            case 2: // bucket as host
                txtHostname.ReadOnly  = true;
                txtHostname.BackColor = SystemColors.ControlLight;
                txtHostname.Text      = $"https://{_bucket}/";
                break;
        }
    }

    private void RelayoutDynamic()
    {
        int w = ClientSize.Width - 16;
        grpExpiry.Width   = w;
        grpHostname.Width = w;
        txtUrl.Width      = w;

        grpHostname.Top = grpExpiry.Bottom + 4;
        txtUrl.Top      = grpHostname.Bottom + 4;
        txtUrl.Height   = Math.Max(60, Controls.OfType<FlowLayoutPanel>().First().Top - txtUrl.Top - 4);

        // Sync inner control widths
        foreach (Control c in grpExpiry.Controls)   c.Width = grpExpiry.Width  - 16;
        foreach (Control c in grpHostname.Controls) c.Width = grpHostname.Width - 16;
    }

    // ── URL generation ────────────────────────────────────────────────────────
    private void RegenerateUrls()
    {
        DateTime? expiresAt = cboExpiry.SelectedIndex switch
        {
            1 => DateTime.UtcNow.AddMinutes((double)numMinutes.Value),
            2 => dtpExpiry.Value.ToUniversalTime(),
            _ => null
        };

        string? customBase = cboHostname.SelectedIndex switch
        {
            1 => txtHostname.Text.Trim(),   // custom — editable
            2 => $"https://{_bucket}/",     // bucket as host
            _ => null                       // null = use default (let S3Service decide)
        };

        var lines = new List<string>();
        bool https = chkHttps.Checked;

        foreach (var item in _items)
        {
            try
            {
                string url = expiresAt == null
                    ? _s3.GetPublicUrl(_bucket, item.Key, https)
                    : _s3.GetPreSignedUrl(_bucket, item.Key, expiresAt.Value, https);

                // Apply custom hostname if selected
                if (customBase != null && !string.IsNullOrWhiteSpace(customBase))
                    url = customBase.TrimEnd('/') + "/" + item.Key;

                lines.Add(url);
            }
            catch (Exception ex)
            {
                lines.Add($"[Error: {ex.Message}]");
            }
        }

        txtUrl.Text  = string.Join(Environment.NewLine, lines);
        btnCopy.Text = lines.Count > 1 ? $"Copy All ({lines.Count})" : "Copy";
    }

    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(txtUrl.Text))
            Clipboard.SetText(txtUrl.Text);
        Close();
    }
}
