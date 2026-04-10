using S3Lite.Models;
using S3Lite.Services;

namespace S3Lite.Forms;

public class ConnectForm : Form
{
    private ComboBox cboProfiles     = null!;
    private Button   btnDeleteProfile = null!;
    private TextBox  txtProfileName  = null!;
    private ComboBox cboCredType     = null!;
    private TextBox  txtAccessKey    = null!;
    private TextBox  txtSecretKey    = null!;
    private TextBox  txtAwsProfile   = null!;
    private ComboBox cboRegion       = null!;
    private TextBox  txtEndpoint     = null!;
    private CheckBox chkPathStyle    = null!;
    private CheckBox chkDualStack    = null!;
    private CheckBox chkAcceleration = null!;
    private Button   btnOk           = null!;
    private Button   btnCancel       = null!;

    // Labels we need to enable/disable with the fields
    private Label lblAccessKey  = null!;
    private Label lblSecretKey  = null!;
    private Label lblAwsProfile = null!;

    private List<S3Connection> _profiles = [];

    public S3Connection Result { get; private set; } = new();

    public ConnectForm(S3Connection? existing = null)
    {
        InitializeComponent();
        LoadProfiles(existing?.ProfileName);
        if (existing != null) FillFields(existing);
    }

    private void InitializeComponent()
    {
        Text            = "Connect to S3";
        Size            = new Size(430, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // 3 columns: label | field | optional small button
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            Padding     = new Padding(12),
            ColumnCount = 3,
            RowCount    = 13,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); // label
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));  // field
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));  // optional button

        // ── Saved profiles ────────────────────────────────────────────────────
        cboProfiles = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cboProfiles.SelectedIndexChanged += CboProfiles_SelectedIndexChanged;

        btnDeleteProfile = new Button { Text = "✕", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        btnDeleteProfile.Click += BtnDeleteProfile_Click;

        layout.Controls.Add(MakeLabel("Saved profile:"), 0, 0);
        layout.Controls.Add(cboProfiles, 1, 0);
        layout.Controls.Add(btnDeleteProfile, 2, 0);

        // ── Separator ─────────────────────────────────────────────────────────
        var sep = new Label
        {
            Text      = "── New / Edit connection ────────────────",
            Dock      = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding   = new Padding(0, 6, 0, 0)
        };
        layout.Controls.Add(sep, 0, 1);
        layout.SetColumnSpan(sep, 3);

        // ── Profile name ──────────────────────────────────────────────────────
        txtProfileName = new TextBox { Text = "Default", Dock = DockStyle.Fill };
        AddRow(layout, 2, "Profile name:", txtProfileName);

        // ── Credential type ───────────────────────────────────────────────────
        cboCredType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
        cboCredType.Items.AddRange(new[]
        {
            "Access Key (recommended)",
            "Environment Variables",
            "AWS Profile / Config file"
        });
        cboCredType.SelectedIndex = 0;
        cboCredType.SelectedIndexChanged += CboCredType_Changed;
        AddRow(layout, 3, "Credential type:", cboCredType);

        // ── Access key + secret key ───────────────────────────────────────────
        txtAccessKey = new TextBox { Dock = DockStyle.Fill };
        txtSecretKey = new TextBox { PasswordChar = '*', Dock = DockStyle.Fill };
        lblAccessKey = MakeLabel("Access key:");
        lblSecretKey = MakeLabel("Secret key:");

        layout.Controls.Add(lblAccessKey, 0, 4);
        layout.Controls.Add(txtAccessKey, 1, 4);
        layout.SetColumnSpan(txtAccessKey, 2);

        layout.Controls.Add(lblSecretKey, 0, 5);
        layout.Controls.Add(txtSecretKey, 1, 5);
        layout.SetColumnSpan(txtSecretKey, 2);

        // ── AWS profile name (used for AwsProfile credential type) ────────────
        txtAwsProfile = new TextBox { Dock = DockStyle.Fill, Text = "default", Enabled = false };
        lblAwsProfile = MakeLabel("AWS profile:");
        lblAwsProfile.Enabled = false;
        layout.Controls.Add(lblAwsProfile, 0, 6);
        layout.Controls.Add(txtAwsProfile, 1, 6);
        layout.SetColumnSpan(txtAwsProfile, 2);

        // ── Region ────────────────────────────────────────────────────────────
        cboRegion = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
        cboRegion.Items.AddRange(new[]
        {
            "us-east-1","us-east-2","us-west-1","us-west-2",
            "eu-west-1","eu-west-2","eu-west-3","eu-central-1","eu-north-1",
            "ap-northeast-1","ap-northeast-2","ap-southeast-1","ap-southeast-2",
            "ap-south-1","sa-east-1","ca-central-1",
            "cn-north-1","cn-northwest-1",
            "us-gov-east-1","us-gov-west-1"
        });
        cboRegion.Text = "us-east-1";
        AddRow(layout, 7, "Region:", cboRegion);

        // ── Custom endpoint ───────────────────────────────────────────────────
        txtEndpoint = new TextBox { Dock = DockStyle.Fill, PlaceholderText = "https://... (optional)" };
        AddRow(layout, 8, "Custom endpoint:", txtEndpoint);

        // ── Checkboxes ────────────────────────────────────────────────────────
        chkPathStyle    = new CheckBox { Text = "Force path-style addressing",    AutoSize = true };
        chkDualStack    = new CheckBox { Text = "Use dual-stack endpoints (IPv4/IPv6)", AutoSize = true, Checked = true };
        chkAcceleration = new CheckBox { Text = "Use Transfer Acceleration",       AutoSize = true };

        layout.Controls.Add(chkPathStyle,    1, 9);  layout.SetColumnSpan(chkPathStyle,    2);
        layout.Controls.Add(chkDualStack,    1, 10); layout.SetColumnSpan(chkDualStack,    2);
        layout.Controls.Add(chkAcceleration, 1, 11); layout.SetColumnSpan(chkAcceleration, 2);

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock          = DockStyle.Fill,
            AutoSize      = true
        };
        btnCancel = new Button { Text = "Cancel",  DialogResult = DialogResult.Cancel, Width = 80 };
        btnOk     = new Button { Text = "Connect", Width = 80 };
        btnOk.Click += BtnOk_Click;
        btnPanel.Controls.AddRange(new Control[] { btnCancel, btnOk });
        layout.Controls.Add(btnPanel, 0, 12);
        layout.SetColumnSpan(btnPanel, 3);

        Controls.Add(layout);
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        // ── Tooltips ──────────────────────────────────────────────────────────
        var tip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400 };
        tip.SetToolTip(cboCredType,
            "Access Key: enter your AWS Access Key ID and Secret.\n" +
            "Environment Variables: uses AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY from the system.\n" +
            "AWS Profile: uses a named profile from ~/.aws/credentials or AWS credentials store.");
        tip.SetToolTip(txtEndpoint,
            "Use this for S3-compatible services like MinIO, Wasabi, Backblaze B2, etc.\n" +
            "Leave empty to use standard AWS S3.\n" +
            "Example: https://s3.us-west-000.backblazeb2.com");
        tip.SetToolTip(chkPathStyle,
            "Forces the bucket name into the URL path instead of the subdomain.\n" +
            "Required by most S3-compatible services (MinIO, Wasabi, etc.).");
        tip.SetToolTip(chkDualStack,
            "Routes requests through AWS dual-stack endpoints that support both IPv4 and IPv6.");
        tip.SetToolTip(chkAcceleration,
            "Uses AWS S3 Transfer Acceleration for faster uploads/downloads.\n" +
            "Must be enabled on the bucket in the AWS Console first.");
    }

    // ── Credential type change ────────────────────────────────────────────────
    private void CboCredType_Changed(object? sender, EventArgs e)
    {
        bool isAccessKey  = cboCredType.SelectedIndex == 0;
        bool isAwsProfile = cboCredType.SelectedIndex == 2;

        txtAccessKey.Enabled  = isAccessKey;
        txtSecretKey.Enabled  = isAccessKey;
        lblAccessKey.Enabled  = isAccessKey;
        lblSecretKey.Enabled  = isAccessKey;

        txtAwsProfile.Enabled = isAwsProfile;
        lblAwsProfile.Enabled = isAwsProfile;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static Label MakeLabel(string text) =>
        new() { Text = text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Fill };

    private static void AddRow(TableLayoutPanel tbl, int row, string label, Control ctrl)
    {
        tbl.Controls.Add(MakeLabel(label), 0, row);
        tbl.Controls.Add(ctrl, 1, row);
        tbl.SetColumnSpan(ctrl, 2);
    }

    // ── Profile management ────────────────────────────────────────────────────
    private void LoadProfiles(string? selectName = null)
    {
        _profiles = ProfileStore.Load();
        cboProfiles.Items.Clear();
        cboProfiles.Items.Add("— New profile —");
        foreach (var p in _profiles) cboProfiles.Items.Add(p.ProfileName);

        if (selectName != null)
        {
            var idx = cboProfiles.Items.IndexOf(selectName);
            cboProfiles.SelectedIndex = idx >= 0 ? idx : 0;
        }
        else
            cboProfiles.SelectedIndex = _profiles.Count > 0 ? 1 : 0;
    }

    private void CboProfiles_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (cboProfiles.SelectedIndex <= 0) return;
        var profile = _profiles.FirstOrDefault(p => p.ProfileName == cboProfiles.SelectedItem?.ToString());
        if (profile != null) FillFields(profile);
    }

    private void BtnDeleteProfile_Click(object? sender, EventArgs e)
    {
        if (cboProfiles.SelectedIndex <= 0) return;
        var name = cboProfiles.SelectedItem?.ToString();
        if (name == null) return;
        if (MessageBox.Show($"Delete profile '{name}'?", "Delete",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        ProfileStore.Delete(name);
        LoadProfiles();
    }

    // ── OK / build result ─────────────────────────────────────────────────────
    private void BtnOk_Click(object? sender, EventArgs e)
    {
        string credType = cboCredType.SelectedIndex switch
        {
            1 => "EnvVars",
            2 => "AwsProfile",
            _ => "AccessKey"
        };

        if (credType == "AccessKey" &&
            (string.IsNullOrWhiteSpace(txtAccessKey.Text) || string.IsNullOrWhiteSpace(txtSecretKey.Text)))
        {
            MessageBox.Show("Access key and secret key are required.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (credType == "AwsProfile" && string.IsNullOrWhiteSpace(txtAwsProfile.Text))
        {
            MessageBox.Show("Enter an AWS profile name.", "Validation",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result = new S3Connection
        {
            ProfileName    = string.IsNullOrWhiteSpace(txtProfileName.Text) ? "Default" : txtProfileName.Text.Trim(),
            CredentialType = credType,
            AccessKey      = txtAccessKey.Text.Trim(),
            SecretKey      = txtSecretKey.Text,
            AwsProfileName = txtAwsProfile.Text.Trim(),
            Region         = cboRegion.Text.Trim(),
            EndpointUrl    = string.IsNullOrWhiteSpace(txtEndpoint.Text) ? null : txtEndpoint.Text.Trim(),
            ForcePathStyle = chkPathStyle.Checked,
            UseDualStack   = chkDualStack.Checked,
            UseAcceleration = chkAcceleration.Checked,
        };

        ProfileStore.Upsert(Result);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void FillFields(S3Connection c)
    {
        txtProfileName.Text  = c.ProfileName;
        txtAccessKey.Text    = c.AccessKey;
        txtSecretKey.Text    = c.SecretKey;
        txtAwsProfile.Text   = c.AwsProfileName;
        cboRegion.Text       = c.Region;
        txtEndpoint.Text     = c.EndpointUrl ?? "";
        chkPathStyle.Checked    = c.ForcePathStyle;
        chkDualStack.Checked    = c.UseDualStack;
        chkAcceleration.Checked = c.UseAcceleration;

        cboCredType.SelectedIndex = c.CredentialType switch
        {
            "EnvVars"    => 1,
            "AwsProfile" => 2,
            _            => 0
        };
        // Trigger enable/disable update
        CboCredType_Changed(null, EventArgs.Empty);
    }
}
