using S3Lite.Models;
using S3Lite.Services;

namespace S3Lite.Forms;

public class BucketPropertiesForm : Form
{
    private readonly S3Service _s3;
    private readonly string    _bucket;

    private ListView          lvProps   = null!;
    private Label             lblStatus = null!;
    private Button            btnClose  = null!;
    private Button            btnRefresh = null!;

    public BucketPropertiesForm(S3Service s3, string bucket)
    {
        _s3    = s3;
        _bucket = bucket;
        InitializeComponent();
        Load += async (_, _) => await LoadPropertiesAsync();
    }

    private void InitializeComponent()
    {
        Text            = $"Properties — {_bucket}";
        Size            = new Size(560, 640);
        MinimumSize     = new Size(420, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = true;
        MinimizeBox     = false;

        // ── Property grid ─────────────────────────────────────────────────────
        lvProps = new ListView
        {
            Dock          = DockStyle.Fill,
            View          = View.Details,
            FullRowSelect = true,
            GridLines     = true,
            MultiSelect   = false,
            BorderStyle   = BorderStyle.None,
            HeaderStyle   = ColumnHeaderStyle.Nonclickable,
        };
        lvProps.Columns.Add("Property", 180);
        lvProps.Columns.Add("Value", 340);

        // ── Context menu ──────────────────────────────────────────────────────
        var ctxMenu      = new ContextMenuStrip();
        var miCopyValue  = new ToolStripMenuItem("Copy Value");
        var miCopyLine   = new ToolStripMenuItem("Copy Property and Value");
        ctxMenu.Items.Add(miCopyValue);
        ctxMenu.Items.Add(miCopyLine);

        ctxMenu.Opening += (_, _) =>
        {
            bool hasItem = lvProps.SelectedItems.Count > 0 &&
                           !string.IsNullOrWhiteSpace(lvProps.SelectedItems[0].Text);
            ctxMenu.Items[0].Enabled = hasItem;
            ctxMenu.Items[1].Enabled = hasItem;
        };
        miCopyValue.Click += (_, _) =>
        {
            if (lvProps.SelectedItems.Count > 0)
                Clipboard.SetText(lvProps.SelectedItems[0].SubItems[1].Text);
        };
        miCopyLine.Click += (_, _) =>
        {
            if (lvProps.SelectedItems.Count > 0)
            {
                var lvi = lvProps.SelectedItems[0];
                Clipboard.SetText($"{lvi.Text}: {lvi.SubItems[1].Text}");
            }
        };
        lvProps.ContextMenuStrip = ctxMenu;

        // Select item under cursor on right-click so SelectedItems is populated when menu opens
        lvProps.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = lvProps.HitTest(e.Location);
                if (hit.Item != null)
                    hit.Item.Selected = true;
            }
        };

        // ── Bottom panel ──────────────────────────────────────────────────────
        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 36 };

        lblStatus = new Label
        {
            Left      = 8, Top = 10,
            Width     = 300, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 7.5f)
        };

        btnRefresh = new Button { Text = "↺ Refresh", Width = 80, Height = 26 };
        btnClose   = new Button { Text = "✖ Close",   Width = 80, Height = 26,
                                  DialogResult = DialogResult.Cancel };
        btnRefresh.Left = 370; btnRefresh.Top = 4;
        btnClose.Left   = 460; btnClose.Top   = 4;
        btnRefresh.Click += async (_, _) => await LoadPropertiesAsync();

        pnlBottom.Controls.AddRange(new Control[] { lblStatus, btnRefresh, btnClose });

        Controls.Add(lvProps);
        Controls.Add(pnlBottom);
        CancelButton = btnClose;
    }

    private async Task LoadPropertiesAsync()
    {
        lvProps.Items.Clear();
        lvProps.Items.Add(new ListViewItem(new[] { "Loading…", "Please wait" })
            { ForeColor = SystemColors.GrayText });
        btnRefresh.Enabled = false;
        lblStatus.Text     = "Loading properties…";
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var p = await _s3.GetBucketPropertiesAsync(_bucket);
            sw.Stop();
            Populate(p);
            lblStatus.Text = $"Loaded in {sw.Elapsed.TotalSeconds:F1}s";
        }
        catch (Exception ex)
        {
            lvProps.Items.Clear();
            lvProps.Items.Add(new ListViewItem(new[] { "Error", ex.Message })
                { ForeColor = Color.Firebrick });
            lblStatus.Text = "Failed to load properties.";
        }
        finally
        {
            btnRefresh.Enabled = true;
        }
    }

    private void Populate(BucketProperties p)
    {
        lvProps.BeginUpdate();
        lvProps.Items.Clear();

        // ── Identity & stats ──────────────────────────────────────────────────
        Add("Name",          p.Name);
        Add("Owner",         Truncate(p.OwnerId, 40));
        Add("Creation date", p.CreationDate.HasValue
            ? p.CreationDate.Value.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt")
            : "—");
        Add("Location",      string.IsNullOrEmpty(p.Region) ? "—" : p.Region);

        AddSeparator();
        Add("Total objects", p.TotalObjects.ToString("N0"));
        Add("Total files",   p.TotalFiles.ToString("N0"));
        Add("Total folders", p.TotalFolders.ToString("N0"));
        Add("Total size",    $"{FormatBytes(p.TotalSize)} ({p.TotalSize:N0} bytes)");
        Add("Uncompleted multipart uploads", p.UncompletedMultipartUploads.ToString());

        // ── Configuration ─────────────────────────────────────────────────────
        AddSeparator();
        AddStatus("Versioning",          p.Versioning);
        AddStatus("Logging",             p.Logging);
        AddStatus("Object Lock",         p.ObjectLock);
        AddStatus("Cross-region replication", p.Replication);
        AddStatus("Transfer Acceleration",    p.TransferAcceleration);
        AddStatus("Server-side encryption",   p.Encryption);
        AddStatus("Requester pays",      p.RequesterPays);

        // ── File details ──────────────────────────────────────────────────────
        if (p.FileTypes.Count > 0 || p.StorageClasses.Count > 0 || p.ModifiedFrom.HasValue)
        {
            AddSeparator();
            if (p.FileTypes.Count > 0)
                Add("File types", string.Join(", ", p.FileTypes));
            if (p.ModifiedFrom.HasValue)
            {
                string range = p.ModifiedFrom == p.ModifiedTo
                    ? p.ModifiedFrom.Value.ToLocalTime().ToString("M/d/yyyy h:mm:ss tt")
                    : $"{p.ModifiedFrom.Value.ToLocalTime():M/d/yyyy h:mm:ss tt} — {p.ModifiedTo!.Value.ToLocalTime():M/d/yyyy h:mm:ss tt}";
                Add("Last modified", range);
            }
            if (p.StorageClasses.Count > 0)
                Add("Storage classes", string.Join(", ", p.StorageClasses));
        }

        lvProps.EndUpdate();

        // Resize Value column to fill available width
        lvProps.Columns[1].Width = -2;
    }

    private void Add(string property, string value)
    {
        lvProps.Items.Add(new ListViewItem(new[] { property, value }));
    }

    private void AddStatus(string property, string value)
    {
        bool isEnabled  = value.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase);
        bool isDisabled = value.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        var lvi = new ListViewItem(new[] { property, value });
        if (isEnabled)       lvi.SubItems[1].ForeColor = Color.DarkGreen;
        else if (isDisabled) lvi.SubItems[1].ForeColor = SystemColors.GrayText;
        lvProps.Items.Add(lvi);
    }

    private void AddSeparator()
    {
        var sep = new ListViewItem(new[] { "", "" })
        {
            BackColor = SystemColors.ControlLight,
            ForeColor = SystemColors.ControlLight
        };
        lvProps.Items.Add(sep);
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..max] + "…" : s;

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        if (bytes >= 1024 * 1024)         return $"{bytes / 1024.0 / 1024.0:F2} MB";
        if (bytes >= 1024)                return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
