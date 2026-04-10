using S3Lite.Services;

namespace S3Lite.Forms;

/// <summary>Dialog to add an external (not owned) S3 bucket by name.</summary>
public class AddExternalBucketForm : Form
{
    private readonly S3Service _s3;

    private TextBox txtBucket = null!;
    private TextBox txtRegion = null!;
    private Button  btnDetect = null!;
    private Button  btnOk     = null!;
    private Label   lblStatus = null!;

    public string BucketName   { get; private set; } = "";
    public string BucketRegion { get; private set; } = "us-east-1";

    public AddExternalBucketForm(S3Service s3)
    {
        _s3 = s3;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text            = "Add External Bucket";
        Size            = new Size(420, 220);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // ── Layout ────────────────────────────────────────────────────────────
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            Padding     = new Padding(12),
            ColumnCount = 2,
            RowCount    = 5,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // input
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); // button

        // Row 0 — Bucket name label
        var lblBucket = new Label
        {
            Text      = "Bucket name:",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };
        layout.Controls.Add(lblBucket, 0, 0);
        layout.SetColumnSpan(lblBucket, 2);

        // Row 1 — Bucket name input (spans both columns)
        txtBucket = new TextBox { Dock = DockStyle.Fill };
        txtBucket.TextChanged += (_, _) => btnOk.Enabled = txtBucket.Text.Trim().Length > 0;
        layout.Controls.Add(txtBucket, 0, 1);
        layout.SetColumnSpan(txtBucket, 2);

        // Row 2 — Region label
        var lblRegion = new Label
        {
            Text      = "Region:",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft
        };
        layout.Controls.Add(lblRegion, 0, 2);
        layout.SetColumnSpan(lblRegion, 2);

        // Row 3 — Region input + Auto-detect button
        txtRegion = new TextBox { Dock = DockStyle.Fill, Text = "us-east-1" };
        layout.Controls.Add(txtRegion, 0, 3);

        btnDetect = new Button { Text = "Auto-detect", Dock = DockStyle.Fill };
        btnDetect.Click += BtnDetect_Click;
        layout.Controls.Add(btnDetect, 1, 3);

        // Row 4 — Status label
        lblStatus = new Label
        {
            Dock      = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(lblStatus, 0, 4);
        layout.SetColumnSpan(lblStatus, 2);

        // Row heights
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // label
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // bucket input
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // label
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));   // region input
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // status (fills rest)

        Controls.Add(layout);

        // ── Bottom buttons ────────────────────────────────────────────────────
        var pnlButtons = new Panel { Dock = DockStyle.Bottom, Height = 40 };

        btnOk = new Button
        {
            Text         = "✔ Add",
            Width        = 80, Height = 28,
            DialogResult = DialogResult.OK,
            Enabled      = false
        };
        btnOk.Left = 222; btnOk.Top = 6;
        btnOk.Click += (_, _) =>
        {
            BucketName   = txtBucket.Text.Trim();
            BucketRegion = txtRegion.Text.Trim();
            if (string.IsNullOrEmpty(BucketRegion)) BucketRegion = "us-east-1";
        };

        var btnCancel = new Button
        {
            Text         = "✖ Cancel",
            Width        = 80, Height = 28,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.Left = 310; btnCancel.Top = 6;

        pnlButtons.Controls.AddRange(new Control[] { btnOk, btnCancel });
        Controls.Add(pnlButtons);

        AcceptButton = btnOk;
        CancelButton = btnCancel;
    }

    private async void BtnDetect_Click(object? sender, EventArgs e)
    {
        string name = txtBucket.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            lblStatus.Text      = "Enter a bucket name first.";
            lblStatus.ForeColor = Color.Firebrick;
            return;
        }

        btnDetect.Enabled   = false;
        lblStatus.ForeColor = SystemColors.GrayText;
        lblStatus.Text      = "Detecting region…";

        try
        {
            string region       = await _s3.GetBucketRegionAsync(name);
            txtRegion.Text      = region;
            lblStatus.Text      = $"Detected region: {region}";
            lblStatus.ForeColor = Color.DarkGreen;
        }
        catch (Exception ex)
        {
            lblStatus.Text      = $"Could not detect: {ex.Message}";
            lblStatus.ForeColor = Color.Firebrick;
        }
        finally
        {
            btnDetect.Enabled = true;
        }
    }
}
