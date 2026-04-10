using S3Lite.Models;
using S3Lite.Services;

namespace S3Lite.Forms;

public class CompressionRulesForm : Form
{
    private ListView lvRules  = null!;
    private Button   btnAdd   = null!;
    private Button   btnEdit  = null!;
    private Button   btnDelete = null!;
    private Button   btnSave  = null!;
    private Button   btnCancel = null!;
    private Label    lblInfo  = null!;

    private readonly List<CompressionRule> _rules;

    public CompressionRulesForm()
    {
        _rules = CompressionRuleStore.Load();
        InitializeComponent();
        RefreshList();
    }

    private void InitializeComponent()
    {
        Text            = "GZip Compression Rules";
        Size            = new Size(680, 480);
        MinimumSize     = new Size(560, 380);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false; MinimizeBox = false;

        // ── Info label ────────────────────────────────────────────────────────
        lblInfo = new Label
        {
            Text      = "Files matching an enabled rule are compressed with GZip before uploading. " +
                        "Content-Encoding: gzip is set automatically so browsers decompress them.",
            Left = 10, Top = 10, Width = 642, Height = 32,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(SystemFonts.DefaultFont.FontFamily, 7.5f)
        };
        Controls.Add(lblInfo);

        // ── Rules list ────────────────────────────────────────────────────────
        lvRules = new ListView
        {
            Left = 10, Top = 48, Width = 642, Height = 320,
            View = View.Details, FullRowSelect = true,
            GridLines = true, MultiSelect = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        lvRules.Columns.Add("",           28);   // enabled checkbox indicator
        lvRules.Columns.Add("Bucket",    160);
        lvRules.Columns.Add("File mask", 180);
        lvRules.Columns.Add("Level",      60);
        lvRules.Columns.Add("Status",     90);
        lvRules.DoubleClick += (_, _) => DoEdit();
        Controls.Add(lvRules);

        // ── Buttons ───────────────────────────────────────────────────────────
        btnAdd    = new Button { Text = "+ Add",    Width = 80,  Height = 28 };
        btnEdit   = new Button { Text = "✎ Edit",   Width = 80,  Height = 28 };
        btnDelete = new Button { Text = "✖ Delete", Width = 80,  Height = 28 };
        btnSave   = new Button { Text = "✔ Save",   Width = 100, Height = 28, DialogResult = DialogResult.OK };
        btnCancel = new Button { Text = "✖ Cancel", Width = 90,  Height = 28, DialogResult = DialogResult.Cancel };

        btnAdd.Click    += (_, _) => DoAdd();
        btnEdit.Click   += (_, _) => DoEdit();
        btnDelete.Click += (_, _) => DoDelete();
        btnSave.Click   += (_, _) => { CompressionRuleStore.Save(_rules); Close(); };

        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom, Height = 44
        };
        pnlBottom.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDelete, btnSave, btnCancel });

        // Position buttons on resize
        pnlBottom.Layout += (_, _) =>
        {
            btnAdd.Left    = 10;  btnAdd.Top    = 8;
            btnEdit.Left   = 98;  btnEdit.Top   = 8;
            btnDelete.Left = 186; btnDelete.Top = 8;
            btnSave.Left   = pnlBottom.Width - 200; btnSave.Top   = 8;
            btnCancel.Left = pnlBottom.Width - 96;  btnCancel.Top = 8;
        };

        Controls.Add(pnlBottom);
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void RefreshList()
    {
        lvRules.Items.Clear();
        foreach (var rule in _rules)
        {
            var lvi = new ListViewItem(rule.Enabled ? "✔" : "")
            {
                ForeColor = rule.Enabled ? SystemColors.ControlText : SystemColors.GrayText
            };
            lvi.SubItems.Add(string.IsNullOrEmpty(rule.BucketMask) || rule.BucketMask == "*" ? "(all buckets)" : rule.BucketMask);
            lvi.SubItems.Add(string.IsNullOrEmpty(rule.FileMask)   || rule.FileMask   == "*" ? "(all files)"   : rule.FileMask);
            lvi.SubItems.Add(rule.Level.ToString());
            lvi.SubItems.Add(rule.Enabled ? "Enabled" : "Disabled");
            lvi.Tag = rule;
            lvRules.Items.Add(lvi);
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool sel = lvRules.SelectedItems.Count > 0;
        btnEdit.Enabled   = sel;
        btnDelete.Enabled = sel;
    }

    private void DoAdd()
    {
        using var dlg = new CompressionRuleEditForm();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _rules.Add(dlg.Result);
            RefreshList();
        }
    }

    private void DoEdit()
    {
        if (lvRules.SelectedItems.Count == 0) return;
        var rule = (CompressionRule)lvRules.SelectedItems[0].Tag!;
        using var dlg = new CompressionRuleEditForm(rule);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            int idx = _rules.IndexOf(rule);
            _rules[idx] = dlg.Result;
            RefreshList();
        }
    }

    private void DoDelete()
    {
        if (lvRules.SelectedItems.Count == 0) return;
        var rule = (CompressionRule)lvRules.SelectedItems[0].Tag!;
        if (MessageBox.Show($"Delete rule for bucket '{rule.BucketMask}' / file '{rule.FileMask}'?",
            "Delete Rule", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
        {
            _rules.Remove(rule);
            RefreshList();
        }
    }
}
