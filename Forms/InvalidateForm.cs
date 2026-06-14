using S3Lite.Models;
using S3Lite.Services;

namespace S3Lite.Forms;

public class InvalidateForm : Form
{
    private readonly S3Service   _s3;
    private readonly string      _bucket;
    private readonly List<string> _keys;
    private readonly AppSettings _settings;

    private List<CfDistribution> _matches = new();

    private TextBox  txtPaths    = null!;
    private ComboBox cmbDist     = null!;
    private TextBox  txtManual   = null!;
    private Button   btnVerify   = null!;
    private Label    lblManual   = null!;
    private CheckBox chkWildcard = null!;
    private Label    lblStatus   = null!;
    private Button   btnInvalidate = null!;

    public InvalidateForm(S3Service s3, string bucket, IEnumerable<string> keys, AppSettings settings)
    {
        _s3       = s3;
        _bucket   = bucket;
        _keys     = keys.ToList();
        _settings = settings;
        InitializeComponent();
        Load += async (_, _) => await DetectAsync();
    }

    private void InitializeComponent()
    {
        Text            = $"Invalidate CloudFront Cache — {_bucket}";
        Size            = new Size(560, 420);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        var previewPaths = S3Service.KeysToInvalidationPaths(_keys);
        Controls.Add(new Label
        {
            Text  = "Paths to invalidate (one per line — edit, add, or remove as needed):",
            Left  = 14, Top = 12, Width = 510, Height = 18
        });

        txtPaths = new TextBox
        {
            Left = 14, Top = 32, Width = 510, Height = 90,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            AcceptsReturn = true,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = string.Join(Environment.NewLine, previewPaths)
        };
        Controls.Add(txtPaths);

        // ── Distribution selection ────────────────────────────────────────────
        Controls.Add(new Label
        {
            Text = "Distribution:", Left = 14, Top = 136, Width = 90, Height = 22
        });

        cmbDist = new ComboBox
        {
            Left = 106, Top = 132, Width = 418, Height = 24,
            DropDownStyle = ComboBoxStyle.DropDownList,
            Visible = false
        };
        cmbDist.SelectedIndexChanged += CmbDist_SelectedIndexChanged;
        Controls.Add(cmbDist);

        lblManual = new Label
        {
            Text = "Distribution ID:", Left = 14, Top = 168, Width = 90, Height = 22,
            Visible = false
        };
        Controls.Add(lblManual);

        txtManual = new TextBox
        {
            Left = 106, Top = 164, Width = 300, Height = 24,
            PlaceholderText = "e.g. E1A2B3C4D5E6F7",
            Visible = false
        };
        Controls.Add(txtManual);

        btnVerify = new Button
        {
            Text = "Verify", Left = 414, Top = 163, Width = 90, Height = 26,
            Visible = false
        };
        btnVerify.Click += async (_, _) => await VerifyManualAsync();
        Controls.Add(btnVerify);

        chkWildcard = new CheckBox
        {
            Text  = "Invalidate the entire distribution (/*) instead of the paths above",
            Left  = 14, Top = 204, Width = 510, Height = 20
        };
        chkWildcard.CheckedChanged += (_, _) =>
        {
            txtPaths.Enabled = !chkWildcard.Checked;
            txtPaths.BackColor = chkWildcard.Checked
                ? SystemColors.Control : SystemColors.Window;
        };
        Controls.Add(chkWildcard);

        Controls.Add(new Panel
        {
            Left = 10, Top = 234, Width = 514, Height = 1, BackColor = SystemColors.ControlDark
        });

        Controls.Add(new Label
        {
            Text = "ℹ AWS includes 1,000 invalidation paths free per month, then $0.005 per path.\n" +
                   "A /* wildcard counts as a single path. Changes take a few minutes to propagate.",
            Left = 14, Top = 242, Width = 510, Height = 32,
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 7.5f)
        });

        lblStatus = new Label
        {
            Text = "Detecting distribution…",
            Left = 14, Top = 286, Width = 510, Height = 48,
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblStatus);

        btnInvalidate = new Button
        {
            Text = "✔ Invalidate", Width = 110, Height = 28,
            Left = 304, Top = 342, Enabled = false
        };
        btnInvalidate.Click += BtnInvalidate_Click;

        var btnClose = new Button
        {
            Text = "Close", Width = 90, Height = 28,
            Left = 424, Top = 342, DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { btnInvalidate, btnClose });
        CancelButton = btnClose;
    }

    // ── Detection ─────────────────────────────────────────────────────────────
    private async Task DetectAsync()
    {
        if (!_s3.IsCloudFrontAvailable)
        {
            SetStatus("CloudFront is only available for AWS S3, not custom endpoints.", error: true);
            return;
        }

        try
        {
            _matches = await _s3.FindDistributionsForBucketAsync(_bucket);

            // Prefer a previously remembered distribution for this bucket.
            _settings.CloudFrontDistributions.TryGetValue(_bucket, out var cachedId);

            if (_matches.Count > 0)
            {
                cmbDist.Items.Clear();
                foreach (var d in _matches)
                    cmbDist.Items.Add(DescribeDist(d));
                cmbDist.Items.Add("Other (enter ID manually)…");
                cmbDist.Visible = true;

                int cachedIdx = cachedId != null ? _matches.FindIndex(d => d.Id == cachedId) : -1;
                cmbDist.SelectedIndex = cachedIdx >= 0 ? cachedIdx : 0;
            }
            else if (!string.IsNullOrEmpty(cachedId))
            {
                ShowManual();
                txtManual.Text = cachedId;
                await VerifyManualAsync();
            }
            else
            {
                ShowManual();
                SetStatus("No distribution auto-detected for this bucket. Enter a Distribution ID and Verify.");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not list distributions: {FriendlyError(ex)}", error: true);
            ShowManual();
        }
    }

    private void CmbDist_SelectedIndexChanged(object? sender, EventArgs e)
    {
        bool isOther = cmbDist.SelectedIndex == _matches.Count;
        if (isOther)
        {
            ShowManual();
            btnInvalidate.Enabled = false;
            SetStatus("Enter a Distribution ID and Verify.");
        }
        else
        {
            lblManual.Visible = txtManual.Visible = btnVerify.Visible = false;
            btnInvalidate.Enabled = true;
            SetStatus($"Ready. Target: {_matches[cmbDist.SelectedIndex].Id}");
        }
    }

    private void ShowManual()
    {
        lblManual.Visible = txtManual.Visible = btnVerify.Visible = true;
    }

    private async Task VerifyManualAsync()
    {
        string id = txtManual.Text.Trim();
        if (string.IsNullOrEmpty(id)) { SetStatus("Enter a Distribution ID first.", error: true); return; }

        btnVerify.Enabled = false;
        SetStatus("Verifying…");
        try
        {
            var dist = await _s3.GetDistributionAsync(id);
            // Replace/append into _matches so invalidate uses the verified entry.
            int existing = _matches.FindIndex(d => d.Id == dist.Id);
            if (existing >= 0) _matches[existing] = dist;
            else _matches.Add(dist);

            _manualVerified = dist;
            btnInvalidate.Enabled = true;
            SetStatus($"Verified: {DescribeDist(dist)}", success: true);
        }
        catch (Exception ex)
        {
            _manualVerified = null;
            btnInvalidate.Enabled = false;
            SetStatus($"Could not verify: {FriendlyError(ex)}", error: true);
        }
        finally { btnVerify.Enabled = true; }
    }

    private CfDistribution? _manualVerified;

    // ── Invalidate ────────────────────────────────────────────────────────────
    private CfDistribution? CurrentDistribution()
    {
        if (txtManual.Visible && _manualVerified != null) return _manualVerified;
        if (cmbDist.Visible && cmbDist.SelectedIndex >= 0 && cmbDist.SelectedIndex < _matches.Count)
            return _matches[cmbDist.SelectedIndex];
        return null;
    }

    private async void BtnInvalidate_Click(object? sender, EventArgs e)
    {
        var dist = CurrentDistribution();
        if (dist == null) { SetStatus("Select or verify a distribution first.", error: true); return; }

        List<string> paths;
        if (chkWildcard.Checked)
        {
            paths = new List<string> { "/*" };
        }
        else
        {
            paths = txtPaths.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();
            if (paths.Count == 0)
            {
                SetStatus("Enter at least one path, or use the /* option.", error: true);
                return;
            }
        }

        btnInvalidate.Enabled = false;
        SetStatus("Submitting invalidation…");
        try
        {
            string invId = await _s3.CreateInvalidationAsync(dist.Id, paths, dist.OriginPath);

            // Remember this distribution for the bucket.
            _settings.CloudFrontDistributions[_bucket] = dist.Id;
            SettingsStore.Save(_settings);

            SetStatus($"Invalidation {invId} created on {dist.Id}.\n" +
                      $"Status: InProgress — typically completes in a few minutes.", success: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Invalidation failed: {FriendlyError(ex)}", error: true);
        }
        finally { btnInvalidate.Enabled = true; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string DescribeDist(CfDistribution d)
    {
        string label = !string.IsNullOrWhiteSpace(d.Comment) ? d.Comment : d.DomainName;
        return $"{d.Id} — {label}";
    }

    private static string FriendlyError(Exception ex)
    {
        if (ex is Amazon.CloudFront.AmazonCloudFrontException cfx)
        {
            if (cfx.ErrorCode is "AccessDenied" or "AccessDeniedException")
                return "access denied — your IAM user needs cloudfront:ListDistributions and " +
                       "cloudfront:CreateInvalidation.";
            if (cfx.ErrorCode == "NoSuchDistribution")
                return "no distribution with that ID exists.";
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
