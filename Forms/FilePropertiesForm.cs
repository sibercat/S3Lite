using S3Lite.Services;

namespace S3Lite.Forms;

public class FilePropertiesForm : Form
{
    private readonly S3Service _s3;
    private readonly string    _bucket;
    private readonly string    _key;

    private ListView lvProps    = null!;
    private Label    lblStatus  = null!;
    private Button   btnClose   = null!;
    private Button   btnRefresh = null!;

    public FilePropertiesForm(S3Service s3, string bucket, string key)
    {
        _s3     = s3;
        _bucket = bucket;
        _key    = key;
        InitializeComponent();
        Load += async (_, _) => await LoadPropertiesAsync();
    }

    private void InitializeComponent()
    {
        string name = _key.Contains('/') ? _key[(_key.LastIndexOf('/') + 1)..] : _key;
        Text            = $"Properties — {name}";
        Size            = new Size(600, 520);
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
        lvProps.Columns.Add("Property", 200);
        lvProps.Columns.Add("Value",    360);

        // ── Context menu ──────────────────────────────────────────────────────
        var ctxMenu     = new ContextMenuStrip();
        var miCopyValue = new ToolStripMenuItem("Copy Value");
        var miCopyLine  = new ToolStripMenuItem("Copy Property and Value");
        ctxMenu.Items.Add(miCopyValue);
        ctxMenu.Items.Add(miCopyLine);

        ctxMenu.Opening += (_, _) =>
        {
            bool hasItem = lvProps.SelectedItems.Count > 0 &&
                           !string.IsNullOrWhiteSpace(lvProps.SelectedItems[0].Text);
            miCopyValue.Enabled = hasItem;
            miCopyLine.Enabled  = hasItem;
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

        lvProps.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                var hit = lvProps.HitTest(e.Location);
                if (hit.Item != null) hit.Item.Selected = true;
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
        btnRefresh.Left = 410; btnRefresh.Top = 4;
        btnClose.Left   = 500; btnClose.Top   = 4;
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
            var rows = await _s3.GetFilePropertiesAsync(_bucket, _key);
            sw.Stop();
            Populate(rows);
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

    private void Populate(List<(string Prop, string Val, bool Highlight)> rows)
    {
        lvProps.BeginUpdate();
        lvProps.Items.Clear();

        foreach (var (prop, val, highlight) in rows)
        {
            if (prop == "---")
            {
                var sep = new ListViewItem(new[] { "", "" })
                {
                    BackColor = SystemColors.ControlLight,
                    ForeColor = SystemColors.ControlLight
                };
                lvProps.Items.Add(sep);
                continue;
            }

            var lvi = new ListViewItem(new[] { prop, val });
            if (highlight)
                lvi.SubItems[1].ForeColor = Color.DarkGreen;
            lvProps.Items.Add(lvi);
        }

        lvProps.EndUpdate();
        lvProps.Columns[1].Width = -2;
    }
}
