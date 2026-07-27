using Amazon.S3;
using S3Lite.Services;

namespace S3Lite.Forms;

public class StaticWebsiteForm : Form
{
    private readonly S3Service _s3;
    private readonly string    _bucket;

    private string _region       = "us-east-1";
    private bool   _blockPublic  = true;
    private bool   _hasPolicy    = false;
    private bool   _wasEnabled   = false;

    // Config S3 Lite does not edit but must not destroy on save
    private List<Amazon.S3.Model.RoutingRule> _routingRules = new();
    private string? _redirectAllTo;

    private CheckBox chkEnable      = null!;
    private TextBox  txtIndex       = null!;
    private TextBox  txtError       = null!;
    private TextBox  txtUrl         = null!;
    private Label    lblAccess      = null!;
    private Label    lblAdvanced    = null!;
    private Button   btnMakePublic  = null!;
    private Button   btnCopyUrl     = null!;
    private Button   btnOpenUrl     = null!;
    private Button   btnSave        = null!;
    private Label    lblStatus      = null!;

    public StaticWebsiteForm(S3Service s3, string bucket)
    {
        _s3     = s3;
        _bucket = bucket;
        InitializeComponent();
        Load += async (_, _) => await LoadAsync();
    }

    private void InitializeComponent()
    {
        Text            = $"Static Website Hosting — {_bucket}";
        Size            = new Size(620, 500);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            Padding     = new Padding(12),
            ColumnCount = 2,
            RowCount    = 13,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ── Enable toggle ─────────────────────────────────────────────────────
        chkEnable = new CheckBox
        {
            Text     = "Enable static website hosting",
            AutoSize = true,
            Enabled  = false,
            Margin   = new Padding(0, 2, 0, 6)
        };
        chkEnable.CheckedChanged += (_, _) => UpdateEnabledState();
        AddSpan(layout, 0, chkEnable);

        // ── Documents ─────────────────────────────────────────────────────────
        txtIndex = new TextBox { Dock = DockStyle.Fill, Text = "index.html", Enabled = false };
        txtError = new TextBox { Dock = DockStyle.Fill, Text = "error.html", Enabled = false };
        AddRow(layout, 1, "Index document:", txtIndex);
        AddRow(layout, 2, "Error document:", txtError);

        lblAdvanced = new Label
        {
            AutoSize  = true,
            Visible   = false,
            ForeColor = Color.DarkOrange,
            Font      = new Font(Font.FontFamily, 7.5f, FontStyle.Bold),
            Margin    = new Padding(0, 0, 0, 4)
        };
        AddSpan(layout, 3, lblAdvanced);

        AddSpan(layout, 4, Hint(
            "The index document is served for requests to a folder (e.g. /about/ → /about/index.html).\n" +
            "The error document is optional — leave blank to use the default S3 error page."));

        AddSpan(layout, 5, Separator());

        // ── Public access ─────────────────────────────────────────────────────
        lblAccess = new Label
        {
            Text     = "Checking public access…",
            AutoSize = true,
            Margin   = new Padding(0, 4, 0, 2)
        };
        AddSpan(layout, 6, lblAccess);

        btnMakePublic = new Button
        {
            Text     = "🌐 Make Bucket Public…",
            Width    = 190, Height = 28,
            Enabled  = false,
            Margin   = new Padding(0, 2, 0, 4),
            AutoSize = false
        };
        btnMakePublic.Click += async (_, _) => await ApplyPolicyAsync();
        AddSpan(layout, 7, btnMakePublic);

        AddSpan(layout, 8, Hint(
            "A website endpoint serves anonymous requests, so objects must be publicly readable.\n" +
            "This applies a bucket policy granting s3:GetObject to everyone — the method AWS\n" +
            "recommends over per-file ACLs."));

        AddSpan(layout, 9, Separator());

        // ── Endpoint ──────────────────────────────────────────────────────────
        txtUrl = new TextBox
        {
            Dock      = DockStyle.Fill,
            ReadOnly  = true,
            BackColor = SystemColors.Control
        };
        AddRow(layout, 10, "Website URL:", txtUrl);

        var urlButtons = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize      = true,
            Margin        = new Padding(0, 4, 0, 0)
        };
        btnCopyUrl = new Button { Text = "📋 Copy", Width = 90, Height = 26, Enabled = false };
        btnOpenUrl = new Button { Text = "↗ Open",  Width = 90, Height = 26, Enabled = false };
        btnCopyUrl.Click += (_, _) =>
        {
            if (txtUrl.TextLength > 0) { Clipboard.SetText(txtUrl.Text); SetStatus("URL copied to clipboard."); }
        };
        btnOpenUrl.Click += (_, _) =>
        {
            if (txtUrl.TextLength == 0) return;
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(txtUrl.Text) { UseShellExecute = true });
        };
        urlButtons.Controls.AddRange(new Control[] { btnCopyUrl, btnOpenUrl });
        layout.Controls.Add(urlButtons, 1, 11);

        // ── Status + actions ──────────────────────────────────────────────────
        lblStatus = new Label
        {
            Text      = "Loading…",
            Dock      = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Margin    = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(lblStatus, 0, 12);

        var actions = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize      = true,
            Margin        = new Padding(0, 4, 0, 0)
        };
        var btnClose = new Button { Text = "Close", Width = 85, Height = 28, DialogResult = DialogResult.Cancel };
        btnSave = new Button { Text = "✔ Save", Width = 95, Height = 28, Enabled = false };
        btnSave.Click += async (_, _) => await SaveAsync();
        actions.Controls.AddRange(new Control[] { btnClose, btnSave });
        layout.Controls.Add(actions, 1, 12);

        Controls.Add(layout);
        CancelButton = btnClose;
    }

    // ── Layout helpers ────────────────────────────────────────────────────────
    private static void AddRow(TableLayoutPanel tbl, int row, string label, Control ctrl)
    {
        tbl.Controls.Add(new Label
        {
            Text      = label,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, row);
        tbl.Controls.Add(ctrl, 1, row);
    }

    private static void AddSpan(TableLayoutPanel tbl, int row, Control ctrl)
    {
        tbl.Controls.Add(ctrl, 0, row);
        tbl.SetColumnSpan(ctrl, 2);
    }

    private Label Hint(string text) => new()
    {
        Text      = text,
        AutoSize  = true,
        ForeColor = SystemColors.GrayText,
        Font      = new Font(Font.FontFamily, 7.5f),
        Margin    = new Padding(0, 0, 0, 4)
    };

    private static Panel Separator() => new()
    {
        Height    = 1,
        Dock      = DockStyle.Fill,
        Margin    = new Padding(0, 6, 0, 6),
        BackColor = SystemColors.ControlDark
    };

    // ── Load ──────────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        try
        {
            var cfgTask    = _s3.GetWebsiteConfigAsync(_bucket);
            var regionTask = _s3.GetBucketRegionAsync(_bucket);
            var accessTask = _s3.GetBucketAccessSettingsAsync(_bucket);
            var policyTask = _s3.HasPublicReadPolicyAsync(_bucket);
            await Task.WhenAll(cfgTask, regionTask, accessTask, policyTask);

            var cfg = cfgTask.Result;
            _region        = regionTask.Result;
            _blockPublic   = accessTask.Result.BlockPublicAccess;
            _hasPolicy     = policyTask.Result;
            _wasEnabled    = cfg.Enabled;
            _routingRules  = cfg.RoutingRules;
            _redirectAllTo = cfg.RedirectAllTo;

            chkEnable.Checked = cfg.Enabled;
            txtIndex.Text     = cfg.IndexDocument;
            txtError.Text     = cfg.ErrorDocument;
            txtUrl.Text       = _s3.GetWebsiteEndpoint(_bucket, _region);

            chkEnable.Enabled = true;
            btnSave.Enabled   = true;
            UpdateAdvancedLabel();
            UpdateEnabledState();
            UpdateAccessLabel();
            SetStatus(cfg.Enabled
                ? "Website hosting is enabled."
                : "Website hosting is not enabled for this bucket.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading configuration: {ex.Message}", error: true);
        }
    }

    private void UpdateAdvancedLabel()
    {
        var notes = new List<string>();
        if (_redirectAllTo != null)
            notes.Add($"This bucket redirects all requests to {_redirectAllTo}. Index/error documents " +
                      "do not apply and are not editable here; you can still disable hosting.");
        if (_routingRules.Count > 0)
            notes.Add($"{_routingRules.Count} redirect rule(s) configured — these are preserved on save.");

        lblAdvanced.Text    = string.Join(Environment.NewLine, notes);
        lblAdvanced.Visible = notes.Count > 0;
    }

    private void UpdateEnabledState()
    {
        // In redirect-all mode the document fields are meaningless — keep them
        // locked so Save can't replace that configuration with an index document
        bool editable = chkEnable.Checked && _redirectAllTo == null;
        txtIndex.Enabled   = editable;
        txtError.Enabled   = editable;
        btnCopyUrl.Enabled = chkEnable.Checked;
        btnOpenUrl.Enabled = chkEnable.Checked;
    }

    private void UpdateAccessLabel()
    {
        string blockText  = _blockPublic ? "ON — blocks public access" : "off";
        string policyText = _hasPolicy   ? "applied" : "not applied";
        lblAccess.Text      = $"Block Public Access: {blockText}     ·     Public read policy: {policyText}";
        lblAccess.ForeColor = (_blockPublic || !_hasPolicy) ? Color.Firebrick : Color.DarkGreen;

        // Applying a policy is pointless while Block Public Access is on
        btnMakePublic.Enabled = !_hasPolicy;
        btnMakePublic.Text    = _hasPolicy ? "🌐 Bucket is public" : "🌐 Make Bucket Public…";
    }

    // ── Actions ───────────────────────────────────────────────────────────────
    private async Task ApplyPolicyAsync()
    {
        if (_blockPublic)
        {
            MessageBox.Show(
                "This bucket has Block Public Access enabled, which will reject a public bucket policy.\n\n" +
                "Close this dialog, right-click the bucket → Public Access Settings…, uncheck " +
                "\"Block all public access\", then try again.",
                "Blocked by Public Access Settings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Make every object in '{_bucket}' readable by anyone on the internet?\n\n" +
            "This replaces the bucket policy with one granting s3:GetObject to all principals. " +
            "Only do this for content you intend to publish.",
            "Make Bucket Public",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        btnMakePublic.Enabled = false;
        SetStatus("Applying public read policy…");
        try
        {
            await _s3.ApplyPublicReadPolicyAsync(_bucket);
            _hasPolicy = true;
            UpdateAccessLabel();
            SetStatus("Bucket policy applied — objects are now publicly readable.", success: true);
        }
        catch (Exception ex)
        {
            UpdateAccessLabel();
            SetStatus($"Could not apply policy: {FriendlyError(ex)}", error: true);
        }
    }

    private async Task SaveAsync()
    {
        btnSave.Enabled = false;
        try
        {
            if (chkEnable.Checked)
            {
                if (_redirectAllTo != null)
                {
                    SetStatus($"Bucket redirects all requests to {_redirectAllTo} — nothing to change. " +
                              "Uncheck the box to disable hosting.");
                    return;
                }
                string index = txtIndex.Text.Trim();
                if (index.Length == 0)
                {
                    SetStatus("Enter an index document (e.g. index.html).", error: true);
                    return;
                }
                SetStatus("Enabling website hosting…");
                // Pass existing redirect rules so PutBucketWebsite doesn't drop them
                await _s3.EnableWebsiteAsync(_bucket, index, txtError.Text.Trim(), _routingRules);
                _wasEnabled = true;
                SetStatus(_hasPolicy
                    ? "Website hosting enabled. Your site is live at the URL above."
                    : "Website hosting enabled — but the bucket is not public yet, so requests will "
                      + "return 403. Use Make Bucket Public above.",
                    success: _hasPolicy, error: !_hasPolicy);
            }
            else
            {
                if (!_wasEnabled) { SetStatus("Website hosting is already disabled."); return; }
                SetStatus("Disabling website hosting…");
                await _s3.DisableWebsiteAsync(_bucket);
                _wasEnabled = false;
                SetStatus("Website hosting disabled. The bucket policy was left unchanged.", success: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving: {FriendlyError(ex)}", error: true);
        }
        finally { btnSave.Enabled = true; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string FriendlyError(Exception ex)
    {
        if (ex is AmazonS3Exception s3x)
        {
            if (s3x.ErrorCode == "AccessDenied")
                return "access denied — this is usually Block Public Access. Right-click the bucket → "
                     + "Public Access Settings… and uncheck \"Block all public access\".";
            if (s3x.ErrorCode == "MalformedPolicy")
                return "AWS rejected the bucket policy as malformed.";
        }
        return ex.Message;
    }

    private void SetStatus(string msg, bool error = false, bool success = false)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(msg, error, success)); return; }
        lblStatus.Text      = msg;
        lblStatus.ForeColor = error   ? Color.Firebrick :
                              success ? Color.DarkGreen  :
                                        SystemColors.GrayText;
    }
}
