using S3Lite.Services;

namespace S3Lite.Forms;

public class PublicAccessForm : Form
{
    private readonly S3Service _s3;
    private readonly string    _bucket;

    private CheckBox chkBlockPublic = null!;
    private CheckBox chkDisableAcls = null!;
    private Label    lblStatus      = null!;
    private Button   btnSave        = null!;

    public PublicAccessForm(S3Service s3, string bucket)
    {
        _s3     = s3;
        _bucket = bucket;
        InitializeComponent();
        Load += async (_, _) => await LoadAsync();
    }

    private void InitializeComponent()
    {
        Text            = $"Public Access Settings — {_bucket}";
        Size            = new Size(540, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        chkBlockPublic = new CheckBox
        {
            Text    = "Block all public access",
            Left    = 14, Top = 16,
            Width   = 490, Height = 20,
            Enabled = false
        };
        Controls.Add(new Label
        {
            Text      = "Blocks public ACLs and public bucket policies (all four AWS settings).\n" +
                        "Must be unchecked before files in this bucket can be made public.",
            Left      = 32, Top = 38,
            Width     = 480, Height = 30,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        chkDisableAcls = new CheckBox
        {
            Text    = "Disable Access Control Lists (ACLs)",
            Left    = 14, Top = 78,
            Width   = 490, Height = 20,
            Enabled = false
        };
        Controls.Add(new Label
        {
            Text      = "Sets Object Ownership to \"Bucket owner enforced\". While checked, per-file\n" +
                        "permissions (Edit Permissions / Make Public) cannot be used in this bucket.",
            Left      = 32, Top = 100,
            Width     = 480, Height = 30,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        Controls.Add(new Panel
        {
            Left      = 10, Top = 144,
            Width     = 505, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        Controls.Add(new Label
        {
            Text      = "⚠ To make files public: uncheck both boxes, save, then use\n" +
                        "right-click → Edit Permissions (ACL) → Make Public on the files.",
            Left      = 14, Top = 152,
            Width     = 500, Height = 30,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        });

        lblStatus = new Label
        {
            Text      = "Loading current settings…",
            Left      = 14, Top = 192,
            Width     = 320, Height = 32,
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblStatus);

        btnSave = new Button
        {
            Text    = "✔ Save",
            Width   = 90, Height = 28,
            Left    = 336, Top = 192,
            Enabled = false
        };
        btnSave.Click += BtnSave_Click;

        var btnClose = new Button
        {
            Text         = "Close",
            Width        = 80, Height = 28,
            Left         = 434, Top = 192,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { chkBlockPublic, chkDisableAcls, btnSave, btnClose });
        CancelButton = btnClose;
    }

    private async Task LoadAsync()
    {
        try
        {
            var settings = await _s3.GetBucketAccessSettingsAsync(_bucket);
            chkBlockPublic.Checked = settings.BlockPublicAccess;
            chkDisableAcls.Checked = settings.AclsDisabled;
            chkBlockPublic.Enabled = true;
            chkDisableAcls.Enabled = true;
            btnSave.Enabled        = true;
            SetStatus("");
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading settings: {ex.Message}", error: true);
        }
    }

    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        btnSave.Enabled = false;
        SetStatus("Saving…");
        try
        {
            await _s3.SetBucketAccessSettingsAsync(_bucket,
                new BucketAccessSettings(chkBlockPublic.Checked, chkDisableAcls.Checked));
            SetStatus("Settings saved.", success: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving settings: {ex.Message}", error: true);
        }
        finally { btnSave.Enabled = true; }
    }

    private void SetStatus(string msg, bool error = false, bool success = false)
    {
        lblStatus.Text      = msg;
        lblStatus.ForeColor = error   ? Color.Firebrick :
                              success ? Color.DarkGreen  :
                                        SystemColors.GrayText;
    }
}
