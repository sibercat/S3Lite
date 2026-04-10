using S3Lite.Services;

namespace S3Lite.Forms;

/// <summary>Browse S3 buckets and folders to pick a destination prefix.</summary>
public class S3BrowseForm : Form
{
    private readonly S3Service _s3;

    private TextBox  txtPath   = null!;
    private TreeView tvFolders = null!;
    private Label    lblStatus = null!;
    private Button   btnOk     = null!;

    /// <summary>Selected destination bucket.</summary>
    public string SelectedBucket { get; private set; } = "";
    /// <summary>Selected destination prefix (empty = bucket root).</summary>
    public string SelectedPrefix { get; private set; } = "";

    private const string DummyText = "__dummy__";
    private static TreeNode Dummy() => new(DummyText);

    private readonly string _infoText;

    public S3BrowseForm(S3Service s3, string infoText = "")
    {
        _s3      = s3;
        _infoText = infoText;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text            = "Browse for Folder";
        Size            = new Size(520, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        MinimizeBox     = false;

        // ── Header ────────────────────────────────────────────────────────────
        Controls.Add(new Label
        {
            Text  = "Please select the destination and click OK.",
            Left  = 14, Top = 12,
            Width = 478, Height = 18
        });

        // ── Description label (populated on Load) ─────────────────────────────
        var lblDesc = new Label
        {
            Left      = 14, Top = 34,
            Width     = 478, Height = 34,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 8f)
        };
        Controls.Add(lblDesc);
        Load += (_, _) => lblDesc.Text = _infoText;

        // ── Path box ──────────────────────────────────────────────────────────
        txtPath = new TextBox
        {
            Left      = 14, Top = 74,
            Width     = 478,
            BackColor = SystemColors.Window,
            ReadOnly  = true
        };
        Controls.Add(txtPath);

        Controls.Add(new Panel
        {
            Left      = 0, Top = 100,
            Width     = 520, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        // ── Tree view ─────────────────────────────────────────────────────────
        tvFolders = new TreeView
        {
            Left           = 14, Top = 106,
            Width          = 478, Height = 350,
            HideSelection  = false,
            ShowRootLines  = true,
            ShowLines      = true,
            ShowPlusMinus  = true
        };
        tvFolders.BeforeExpand  += TvFolders_BeforeExpand;
        tvFolders.AfterSelect   += TvFolders_AfterSelect;
        Controls.Add(tvFolders);

        // ── Bottom area ───────────────────────────────────────────────────────
        Controls.Add(new Panel
        {
            Left      = 0, Top = 462,
            Width     = 520, Height = 1,
            BackColor = SystemColors.ControlDark
        });

        var btnNewFolder = new Button
        {
            Text   = "+ Create new folder",
            Left   = 14, Top = 470,
            Width  = 140, Height = 28
        };
        btnNewFolder.Click += BtnNewFolder_Click;

        btnOk = new Button
        {
            Text         = "✔ OK",
            Left         = 320, Top = 470,
            Width        = 80, Height = 28,
            DialogResult = DialogResult.OK,
            Enabled      = false
        };
        btnOk.Click += (_, _) => CommitSelection();

        var btnCancel = new Button
        {
            Text         = "✖ Cancel",
            Left         = 410, Top = 470,
            Width        = 80, Height = 28,
            DialogResult = DialogResult.Cancel
        };

        lblStatus = new Label
        {
            Text      = "Loading…",
            Left      = 14, Top = 508,
            Width     = 478, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        };

        Controls.AddRange(new Control[] { btnNewFolder, btnOk, btnCancel, lblStatus });
        AcceptButton = btnOk;
        CancelButton = btnCancel;

        Load += async (_, _) => await LoadBucketsAsync();
    }

    // ── Load ──────────────────────────────────────────────────────────────────
    private async Task LoadBucketsAsync()
    {
        try
        {
            var buckets = await _s3.ListBucketsAsync();
            tvFolders.BeginUpdate();
            tvFolders.Nodes.Clear();
            foreach (var b in buckets)
            {
                var node = new TreeNode(b) { Tag = (Bucket: b, Prefix: "") };
                node.Nodes.Add(Dummy()); // placeholder so expand arrow shows
                tvFolders.Nodes.Add(node);
            }
            tvFolders.EndUpdate();
            lblStatus.Text = $"Successfully received list of {buckets.Count} buckets";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
    }

    // ── Lazy-expand: load subfolders ──────────────────────────────────────────
    private async void TvFolders_BeforeExpand(object? sender, TreeViewCancelEventArgs e)
    {
        var node = e.Node!;
        if (node.Nodes.Count != 1 || node.Nodes[0].Text != DummyText) return;

        // Cancel the expand now — we'll re-trigger it after async load completes
        e.Cancel = true;
        node.Nodes.Clear();

        var (bucket, prefix) = ((string Bucket, string Prefix))node.Tag!;
        try
        {
            var items = await _s3.ListObjectsAsync(bucket, prefix);
            var folders = items.Where(i => i.Type == Models.S3ItemType.Folder).ToList();
            foreach (var f in folders)
            {
                var child = new TreeNode(f.Name) { Tag = (Bucket: bucket, Prefix: f.Key) };
                child.Nodes.Add(Dummy());
                node.Nodes.Add(child);
            }
            if (folders.Count == 0)
                node.Nodes.Add(new TreeNode("(empty)") { ForeColor = SystemColors.GrayText });
        }
        catch
        {
            node.Nodes.Add(new TreeNode("(error)") { ForeColor = Color.Firebrick });
        }

        // Now expand for real — children are loaded, no dummy node present
        node.Expand();
    }

    private void TvFolders_AfterSelect(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is (string bucket, string prefix))
        {
            txtPath.Text = string.IsNullOrEmpty(prefix) ? bucket : $"{bucket}/{prefix}";
            btnOk.Enabled = true;
        }
        else
        {
            txtPath.Text  = "";
            btnOk.Enabled = false;
        }
    }

    private void CommitSelection()
    {
        if (tvFolders.SelectedNode?.Tag is (string bucket, string prefix))
        {
            SelectedBucket = bucket;
            SelectedPrefix = prefix;
        }
    }

    // ── Create new folder ─────────────────────────────────────────────────────
    private async void BtnNewFolder_Click(object? sender, EventArgs e)
    {
        if (tvFolders.SelectedNode?.Tag is not (string bucket, string prefix))
        {
            MessageBox.Show("Select a bucket or folder first.", "Create Folder",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox("Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;

        string newPrefix = prefix + name.Trim().Trim('/') + "/";
        try
        {
            await _s3.CreateFolderAsync(bucket, newPrefix);

            // Add node and select it
            var child = new TreeNode(name.Trim()) { Tag = (Bucket: bucket, Prefix: newPrefix) };
            child.Nodes.Add(Dummy());

            var parent = tvFolders.SelectedNode;
            if (parent.Nodes.Count == 1 && parent.Nodes[0].Text == "(empty)")
                parent.Nodes.Clear();
            parent.Nodes.Add(child);
            parent.Expand();
            tvFolders.SelectedNode = child;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Create Folder Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
