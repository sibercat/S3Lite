using S3Lite.Services;

namespace S3Lite.Forms;

public class CreateBucketForm : Form
{
    private readonly string _defaultRegion;

    private TextBox      txtName      = null!;
    private ComboBox     cmbRegion    = null!;
    private Panel        _pnlMore     = null!;
    private LinkLabel    _lnkToggle   = null!;
    private LinkLabel    _lnkRetention = null!;
    private CheckBox     _chkObjLock  = null!;
    private bool         _expanded    = false;

    private ObjectLockResult _lockResult = new();

    // Return values
    public string BucketName   { get; private set; } = "";
    public string BucketRegion { get; private set; } = "";
    public bool   BlockPublicAccess { get; private set; }
    public bool   DisableAcls       { get; private set; }
    public bool   ObjectLock        { get; private set; }
    public ObjectLockResult LockResult => _lockResult;

    private static readonly (string Code, string Display)[] Regions =
    {
        ("us-east-1",      "US East (N. Virginia)"),
        ("us-east-2",      "US East (Ohio)"),
        ("us-west-1",      "US West (N. California)"),
        ("us-west-2",      "US West (Oregon)"),
        ("ap-east-1",      "Asia Pacific (Hong Kong)"),
        ("ap-south-1",     "Asia Pacific (Mumbai)"),
        ("ap-northeast-3", "Asia Pacific (Osaka)"),
        ("ap-northeast-2", "Asia Pacific (Seoul)"),
        ("ap-southeast-1", "Asia Pacific (Singapore)"),
        ("ap-southeast-2", "Asia Pacific (Sydney)"),
        ("ap-northeast-1", "Asia Pacific (Tokyo)"),
        ("ca-central-1",   "Canada (Central)"),
        ("eu-central-1",   "Europe (Frankfurt)"),
        ("eu-west-1",      "Europe (Ireland)"),
        ("eu-west-2",      "Europe (London)"),
        ("eu-south-1",     "Europe (Milan)"),
        ("eu-west-3",      "Europe (Paris)"),
        ("eu-north-1",     "Europe (Stockholm)"),
        ("me-south-1",     "Middle East (Bahrain)"),
        ("sa-east-1",      "South America (São Paulo)")
    };

    private const int CollapsedH = 278;
    private const int ExpandedH  = 458;

    public CreateBucketForm(string defaultRegion)
    {
        _defaultRegion = defaultRegion;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text            = "Create New Bucket";
        Size            = new Size(510, CollapsedH);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // ── Top section (always visible) ──────────────────────────────────────
        Controls.Add(new Label { Text = "Bucket name:", Left = 14, Top = 12, Width = 460, Height = 18 });

        txtName = new TextBox { Left = 14, Top = 30, Width = 468, Height = 23 };
        Controls.Add(txtName);

        Controls.Add(new Label
        {
            Text      = "Should contain only lowercase letters, numbers, periods (.) and dashes (-)",
            Left      = 14, Top = 57,
            Width     = 468, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        Controls.Add(new Label { Text = "Bucket region:", Left = 14, Top = 82, Width = 460, Height = 18 });

        cmbRegion = new ComboBox
        {
            Left          = 14, Top = 100,
            Width         = 468,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var (_, display) in Regions)
            cmbRegion.Items.Add(display);

        int defaultIdx = Array.FindIndex(Regions, r => r.Code == _defaultRegion);
        cmbRegion.SelectedIndex = defaultIdx >= 0 ? defaultIdx : 0;
        Controls.Add(cmbRegion);

        Controls.Add(new Label
        {
            Text      = "You can choose the geographical region where your bucket will be created.",
            Left      = 14, Top = 128,
            Width     = 468, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        Controls.Add(new Panel
        {
            Left      = 0, Top = 150,
            Width     = 510, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        _lnkToggle = new LinkLabel
        {
            Text   = "show more settings",
            Left   = 14, Top = 158,
            Width  = 200, Height = 18
        };
        _lnkToggle.LinkClicked += (_, _) => ToggleMore();
        Controls.Add(_lnkToggle);

        // ── More settings panel (hidden by default) ───────────────────────────
        _pnlMore = new Panel
        {
            Left    = 0, Top = 182,
            Width   = 510, Height = 172,
            Visible = false
        };

        _chkObjLock = new CheckBox
        {
            Text  = "Enable S3 Object Lock",
            Left  = 14, Top = 10,
            Width = 240, Height = 20
        };
        _chkObjLock.CheckedChanged += (_, _) =>
        {
            _lockResult.Enabled = _chkObjLock.Checked;
            _lnkRetention.Enabled = _chkObjLock.Checked;
        };

        _lnkRetention = new LinkLabel
        {
            Text    = "configure default retention settings…",
            Left    = 258, Top = 12,
            Width   = 230, Height = 16,
            Enabled = false
        };
        _lnkRetention.LinkClicked += (_, _) => ConfigureRetention();

        _pnlMore.Controls.Add(new Label
        {
            Text      = "You can only enable Object Lock for new buckets. This automatically enables versioning.",
            Left      = 14, Top = 32,
            Width     = 470, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        var chkDisableAcls = new CheckBox
        {
            Text    = "Disable Access Control Lists (ACLs)",
            Left    = 14, Top = 62,
            Width   = 460, Height = 20,
            Checked = true
        };
        _pnlMore.Controls.Add(new Label
        {
            Text      = "Bucket owner automatically owns and has full control over every object in the bucket.",
            Left      = 14, Top = 84,
            Width     = 470, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        var chkBlockPublic = new CheckBox
        {
            Text    = "Block all public access to this bucket",
            Left    = 14, Top = 112,
            Width   = 460, Height = 20,
            Checked = true
        };
        _pnlMore.Controls.Add(new Label
        {
            Text      = "Files cannot be made public either through ACL or Bucket Policies.",
            Left      = 14, Top = 134,
            Width     = 470, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        _pnlMore.Controls.AddRange(new Control[] { _chkObjLock, _lnkRetention, chkDisableAcls, chkBlockPublic });
        Controls.Add(_pnlMore);

        // ── Bottom panel (always visible, docked) ─────────────────────────────
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 56 };
        pnlBottom.Controls.Add(new Panel
        {
            Left      = 0, Top = 0,
            Width     = 510, Height = 1,
            BackColor = SystemColors.ControlDark,
            Dock      = DockStyle.Top
        });

        var btnCreate = new Button
        {
            Text         = "✔ Create new bucket",
            Width        = 140, Height = 28,
            Left         = 242, Top = 14,
            DialogResult = DialogResult.OK
        };
        btnCreate.Click += (_, _) =>
        {
            string name = txtName.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a bucket name.", "Create Bucket",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            BucketName      = name;
            BucketRegion    = Regions[cmbRegion.SelectedIndex].Code;
            BlockPublicAccess = chkBlockPublic.Checked;
            DisableAcls     = chkDisableAcls.Checked;
            ObjectLock      = _chkObjLock.Checked;
        };

        var btnCancel = new Button
        {
            Text         = "✖ Cancel",
            Width        = 90, Height = 28,
            Left         = 392, Top = 14,
            DialogResult = DialogResult.Cancel
        };

        pnlBottom.Controls.AddRange(new Control[] { btnCreate, btnCancel });
        Controls.Add(pnlBottom);

        AcceptButton = btnCreate;
        CancelButton = btnCancel;
    }

    private void ToggleMore()
    {
        _expanded           = !_expanded;
        _pnlMore.Visible    = _expanded;
        _lnkToggle.Text     = _expanded ? "show less settings" : "show more settings";
        Size                = new Size(Width, _expanded ? ExpandedH : CollapsedH);
    }

    private void ConfigureRetention()
    {
        using var dlg = new ObjectLockForm(_lockResult);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _lockResult              = dlg.Result;
        _chkObjLock.Checked     = _lockResult.Enabled;
        _lnkRetention.Enabled   = _chkObjLock.Checked;
    }
}
