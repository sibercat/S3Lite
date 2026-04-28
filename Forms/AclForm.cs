using Amazon.S3;
using Amazon.S3.Model;
using S3Lite.Services;

namespace S3Lite.Forms;

public class AclForm : Form
{
    private readonly S3Service _s3;
    private readonly string _bucket;
    private readonly List<string> _keys;

    // Set on successful load (single-file mode only)
    private Owner?  _owner;
    private List<S3Grant> _loadedGrants = new();

    // [row, col] — row: 0=Owner, 1=Authenticated Users, 2=Everyone
    //              col: 0=Read, 1=Write, 2=Read ACL, 3=Write ACL, 4=Full Control
    private readonly CheckBox[,] _checks = new CheckBox[3, 5];
    private Label  lblStatus  = null!;
    private Button btnSave    = null!;
    private Button _btnPublic  = null!;
    private Button _btnPrivate = null!;

    private const string AuthUri = "http://acs.amazonaws.com/groups/global/AuthenticatedUsers";
    private const string AllUri  = "http://acs.amazonaws.com/groups/global/AllUsers";

    public AclForm(S3Service s3, string bucket, IEnumerable<string> keys)
    {
        _s3     = s3;
        _bucket = bucket;
        _keys   = keys.ToList();
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text            = "Edit Permissions (ACL)";
        Size            = new Size(610, 320);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        string keyLabel = _keys.Count == 1
            ? $"Key: {_keys[0]}"
            : $"Applying to {_keys.Count} files";
        Controls.Add(new Label
        {
            Text      = keyLabel,
            Left      = 10, Top = 10,
            Width     = 575, Height = 18,
            Font      = new Font(Font.FontFamily, 8.5f),
            ForeColor = SystemColors.GrayText
        });

        const int colStart = 170;
        const int colW     = 75;
        const int rowStart = 38;
        const int rowH     = 30;

        string[] colHeaders = { "Read", "Write", "Read ACL", "Write ACL", "Full Control" };
        for (int c = 0; c < colHeaders.Length; c++)
        {
            Controls.Add(new Label
            {
                Text      = colHeaders[c],
                Left      = colStart + c * colW,
                Top       = rowStart,
                Width     = colW, Height = 18,
                TextAlign = ContentAlignment.MiddleCenter,
                Font      = new Font(Font.FontFamily, 8f, FontStyle.Bold)
            });
        }

        string[] rowLabels = { "Owner", "Authenticated Users", "Everyone (public)" };
        for (int r = 0; r < 3; r++)
        {
            Controls.Add(new Label
            {
                Text  = rowLabels[r],
                Left  = 10, Top = rowStart + (r + 1) * rowH + 4,
                Width = 155, Height = 20
            });

            for (int c = 0; c < 5; c++)
            {
                var chk = new CheckBox
                {
                    Left    = colStart + c * colW + (colW - 16) / 2,
                    Top     = rowStart + (r + 1) * rowH + 5,
                    Width   = 20, Height = 20,
                    Enabled = false
                };
                _checks[r, c] = chk;
                Controls.Add(chk);
            }
        }

        Controls.Add(new Panel
        {
            Left      = 10, Top = rowStart + 4 * rowH + 12,
            Width     = 575, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        // Make Public / Make Private shortcut buttons
        var btnPublic = new Button
        {
            Text    = "🌐 Make Public",
            Width   = 120, Height = 28,
            Left    = 10, Top = rowStart + 4 * rowH + 18,
            Enabled = false,
            Tag     = "public"
        };
        var btnPrivate = new Button
        {
            Text    = "🔒 Make Private",
            Width   = 120, Height = 28,
            Left    = 138, Top = rowStart + 4 * rowH + 18,
            Enabled = false,
            Tag     = "private"
        };
        btnPublic.Click  += (_, _) => ApplyPreset(isPublic: true);
        btnPrivate.Click += (_, _) => ApplyPreset(isPublic: false);
        // Store refs so Load can enable them
        _btnPublic  = btnPublic;
        _btnPrivate = btnPrivate;

        Controls.Add(new Panel
        {
            Left      = 10, Top = rowStart + 4 * rowH + 54,
            Width     = 575, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        lblStatus = new Label
        {
            Text      = "Loading ACL…",
            Left      = 10, Top = rowStart + 4 * rowH + 62,
            Width     = 380, Height = 18,
            ForeColor = SystemColors.GrayText
        };
        Controls.Add(lblStatus);

        btnSave = new Button
        {
            Text    = "✔ Save",
            Width   = 90, Height = 28,
            Left    = 404, Top = rowStart + 4 * rowH + 58,
            Enabled = false
        };
        btnSave.Click += BtnSave_Click;

        var btnClose = new Button
        {
            Text         = "Close",
            Width        = 80, Height = 28,
            Left         = 502, Top = rowStart + 4 * rowH + 58,
            DialogResult = DialogResult.Cancel
        };

        Controls.AddRange(new Control[] { btnPublic, btnPrivate, btnSave, btnClose });
        CancelButton = btnClose;

        if (_keys.Count == 1)
            Load += async (_, _) => await LoadAsync();
        else
            Load += (_, _) => EnableMultiMode();
    }

    // ── Load ──────────────────────────────────────────────────────────────────
    private async Task LoadAsync()
    {
        try
        {
            var resp     = await _s3.GetAclAsync(_bucket, _keys[0]);
            _owner        = resp.Owner;
            _loadedGrants = resp.Grants ?? new List<S3Grant>();
            PopulateGrid(_loadedGrants, _owner?.Id ?? "");
            string ownerLabel = _owner?.DisplayName?.Length > 0 ? _owner.DisplayName : (_owner?.Id ?? "Unknown");
            SetStatus($"Owner: {ownerLabel}");
            EnableChecks(true);
            btnSave.Enabled    = true;
            _btnPublic.Enabled  = true;
            _btnPrivate.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading ACL: {ex.Message}", error: true);
        }
    }

    private void EnableMultiMode()
    {
        SetStatus($"Set permissions to apply to all {_keys.Count} files, then press Save.");
        EnableChecks(true);
        btnSave.Enabled    = true;
        _btnPublic.Enabled  = true;
        _btnPrivate.Enabled = true;
    }

    private void PopulateGrid(List<S3Grant> grants, string ownerId)
    {
        bool IsOwner(S3Grant g) => g.Grantee?.Type == GranteeType.CanonicalUser &&
                                   g.Grantee.CanonicalUser == ownerId;
        bool IsAuth(S3Grant g)  => g.Grantee?.Type == GranteeType.Group &&
                                   g.Grantee.URI == AuthUri;
        bool IsAll(S3Grant g)   => g.Grantee?.Type == GranteeType.Group &&
                                   g.Grantee.URI == AllUri;

        Func<S3Grant, bool>[] grantees = { IsOwner, IsAuth, IsAll };

        S3Permission[] perms =
        {
            S3Permission.READ, S3Permission.WRITE,
            S3Permission.READ_ACP, S3Permission.WRITE_ACP
        };

        for (int r = 0; r < 3; r++)
        {
            bool hasFullControl = grants.Any(g => grantees[r](g) && g.Permission == S3Permission.FULL_CONTROL);
            _checks[r, 4].Checked = hasFullControl;

            for (int c = 0; c < 4; c++)
                _checks[r, c].Checked = hasFullControl ||
                                        grants.Any(g => grantees[r](g) && g.Permission == perms[c]);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────
    private async void BtnSave_Click(object? sender, EventArgs e)
    {
        btnSave.Enabled = false;

        if (_keys.Count == 1)
        {
            SetStatus("Saving…");
            try
            {
                var acl = BuildAcl(_owner);
                await _s3.PutAclAsync(_bucket, _keys[0], acl);
                _loadedGrants = acl.Grants ?? new List<S3Grant>();
                SetStatus("Permissions saved.", success: true);
            }
            catch (Exception ex)
            {
                SetStatus($"Error saving ACL: {ex.Message}", error: true);
            }
            finally { btnSave.Enabled = true; }
        }
        else
        {
            int done = 0, failed = 0;
            SetStatus($"Saving 0 / {_keys.Count}…");
            await Task.WhenAll(_keys.Select(async key =>
            {
                try
                {
                    var resp = await _s3.GetAclAsync(_bucket, key);
                    var acl  = BuildAcl(resp.Owner);
                    await _s3.PutAclAsync(_bucket, key, acl);
                    Interlocked.Increment(ref done);
                }
                catch { Interlocked.Increment(ref failed); }
                SetStatus($"Saving {done + failed} / {_keys.Count}…");
            }));
            if (failed == 0)
                SetStatus($"Permissions saved for all {_keys.Count} files.", success: true);
            else
                SetStatus($"Done: {done} saved, {failed} failed.", error: true);
            btnSave.Enabled = true;
        }
    }

    private S3AccessControlList BuildAcl(Owner? owner)
    {
        string ownerId   = owner?.Id          ?? "";
        string ownerName = owner?.DisplayName ?? "";

        // S3Grantee.Type is computed from whichever property is set
        S3Grantee[] grantees =
        {
            new() { CanonicalUser = ownerId, DisplayName = ownerName },
            new() { URI = AuthUri },
            new() { URI = AllUri  }
        };

        S3Permission[] perms =
        {
            S3Permission.READ, S3Permission.WRITE,
            S3Permission.READ_ACP, S3Permission.WRITE_ACP
        };

        var result = new S3AccessControlList { Owner = owner };

        for (int r = 0; r < 3; r++)
        {
            if (_checks[r, 4].Checked)
            {
                result.AddGrant(grantees[r], S3Permission.FULL_CONTROL);
                continue;
            }
            for (int c = 0; c < 4; c++)
            {
                if (_checks[r, c].Checked)
                    result.AddGrant(grantees[r], perms[c]);
            }
        }

        return result;
    }

    // ── Presets ───────────────────────────────────────────────────────────────
    private void ApplyPreset(bool isPublic)
    {
        // Owner → Full Control (both presets)
        _checks[0, 0].Checked = false; // Read
        _checks[0, 1].Checked = false; // Write
        _checks[0, 2].Checked = false; // Read ACL
        _checks[0, 3].Checked = false; // Write ACL
        _checks[0, 4].Checked = true;  // Full Control

        // Authenticated Users → all off (both presets)
        for (int c = 0; c < 5; c++) _checks[1, c].Checked = false;

        // Everyone (public) → Read only if public, all off if private
        _checks[2, 0].Checked = isPublic; // Read
        _checks[2, 1].Checked = false;
        _checks[2, 2].Checked = false;
        _checks[2, 3].Checked = false;
        _checks[2, 4].Checked = false;

        SetStatus(isPublic ? "Public preset applied — press Save to confirm."
                           : "Private preset applied — press Save to confirm.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void SetStatus(string msg, bool error = false, bool success = false)
    {
        if (InvokeRequired) { BeginInvoke(() => SetStatus(msg, error, success)); return; }
        lblStatus.Text      = msg;
        lblStatus.ForeColor = error   ? Color.Firebrick :
                              success ? Color.DarkGreen  :
                                        SystemColors.GrayText;
    }

    private void EnableChecks(bool enabled)
    {
        if (InvokeRequired) { BeginInvoke(() => EnableChecks(enabled)); return; }
        foreach (CheckBox chk in _checks) chk.Enabled = enabled;
    }
}
