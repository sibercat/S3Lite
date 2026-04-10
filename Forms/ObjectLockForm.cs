namespace S3Lite.Forms;

public class ObjectLockResult
{
    public bool    Enabled  { get; set; }
    public string? Mode     { get; set; } // "GOVERNANCE", "COMPLIANCE", or null
    public int?    Days     { get; set; }
    public int?    Years    { get; set; }
}

public class ObjectLockForm : Form
{
    public ObjectLockResult Result { get; private set; }

    private CheckBox      chkEnable  = null!;
    private ComboBox      cmbMode    = null!;
    private NumericUpDown numPeriod  = null!;
    private ComboBox      cmbUnit    = null!;
    private GroupBox      grpMode    = null!;
    private GroupBox      grpPeriod  = null!;

    public ObjectLockForm(ObjectLockResult current)
    {
        Result = new ObjectLockResult
        {
            Enabled = current.Enabled,
            Mode    = current.Mode,
            Days    = current.Days,
            Years   = current.Years
        };
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text            = "Object Lock Configuration";
        Size            = new Size(530, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // ── Enable checkbox ───────────────────────────────────────────────────
        chkEnable = new CheckBox
        {
            Text    = "Enable Object Lock",
            Left    = 14, Top = 12,
            Width   = 460, Height = 20,
            Checked = Result.Enabled
        };
        chkEnable.CheckedChanged += (_, _) => UpdateEnabled();

        var lblEnableHint = new Label
        {
            Text      = "You can only enable Object Lock for new buckets.\r\n" +
                        "If you would like to turn on Object Lock for an existing bucket, please contact AWS Support.\r\n" +
                        "Enabling Object Lock automatically enables versioning.",
            Left      = 14, Top = 36,
            Width     = 488, Height = 52,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        };

        // ── Retention mode ────────────────────────────────────────────────────
        grpMode = new GroupBox
        {
            Text  = "Retention mode:",
            Left  = 12, Top = 95,
            Width = 490, Height = 110
        };

        cmbMode = new ComboBox
        {
            Left          = 10, Top = 22,
            Width         = 464,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbMode.Items.AddRange(new object[] { "Not defined", "Governance mode", "Compliance mode" });

        int modeIdx = Result.Mode == "GOVERNANCE" ? 1 : Result.Mode == "COMPLIANCE" ? 2 : 0;
        cmbMode.SelectedIndex = modeIdx;
        cmbMode.SelectedIndexChanged += (_, _) => UpdateEnabled();

        var lblModeHint = new Label
        {
            Text      = "Retention modes apply different levels of protection to your objects.\r\n" +
                        "You can apply either Governance or Compliance retention mode to any object version.",
            Left      = 10, Top = 52,
            Width     = 464, Height = 42,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        };
        grpMode.Controls.AddRange(new Control[] { cmbMode, lblModeHint });

        // ── Retention period ──────────────────────────────────────────────────
        grpPeriod = new GroupBox
        {
            Text  = "Retention period:",
            Left  = 12, Top = 215,
            Width = 490, Height = 130
        };

        numPeriod = new NumericUpDown
        {
            Left    = 10, Top = 22,
            Width   = 100,
            Minimum = 1, Maximum = 36500,
            Value   = Math.Clamp(Result.Days ?? Result.Years ?? 30, 1, 36500)
        };

        cmbUnit = new ComboBox
        {
            Left          = 118, Top = 22,
            Width         = 100,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbUnit.Items.AddRange(new object[] { "Days", "Years" });
        cmbUnit.SelectedIndex = Result.Years.HasValue ? 1 : 0;

        var lblPeriodHint = new Label
        {
            Text      = "A retention period protects an object version for a fixed amount of time. When you place a\r\n" +
                        "retention period on an object version, Amazon S3 stores a timestamp in the object version's\r\n" +
                        "metadata to indicate when the retention period expires.",
            Left      = 10, Top = 55,
            Width     = 464, Height = 52,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        };
        grpPeriod.Controls.AddRange(new Control[] { numPeriod, cmbUnit, lblPeriodHint });

        // ── Buttons ───────────────────────────────────────────────────────────
        Controls.Add(new Panel
        {
            Left      = 0, Top = 355,
            Width     = 530, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        var btnOk = new Button
        {
            Text         = "✔ OK",
            Width        = 90, Height = 28,
            Left         = 330, Top = 364,
            DialogResult = DialogResult.OK
        };
        btnOk.Click += (_, _) =>
        {
            bool modeSet = cmbMode.SelectedIndex > 0;
            Result = new ObjectLockResult
            {
                Enabled = chkEnable.Checked,
                Mode    = cmbMode.SelectedIndex == 1 ? "GOVERNANCE" :
                          cmbMode.SelectedIndex == 2 ? "COMPLIANCE" : null,
                Days    = modeSet && cmbUnit.SelectedIndex == 0 ? (int)numPeriod.Value : null,
                Years   = modeSet && cmbUnit.SelectedIndex == 1 ? (int)numPeriod.Value : null
            };
        };

        var btnCancel = new Button
        {
            Text         = "✖ Cancel",
            Width        = 90, Height = 28,
            Left         = 428, Top = 364,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[]
        {
            chkEnable, lblEnableHint,
            grpMode, grpPeriod,
            btnOk, btnCancel
        });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        bool on      = chkEnable.Checked;
        bool hasMode = on && cmbMode.SelectedIndex > 0;

        grpMode.Enabled   = on;
        grpPeriod.Enabled = hasMode;
    }
}
