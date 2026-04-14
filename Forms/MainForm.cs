using System.Runtime.InteropServices;
using S3Lite.Helpers;
using S3Lite.Models;
using S3Lite.Services;
using S3Lite.Forms;

namespace S3Lite.Forms;

public class MainForm : Form
{
    // --- Services & state ---
    private S3Service? _s3;
    private TransferManager? _transferManager;
    private S3Connection? _connection;
    private AppSettings _settings  = SettingsStore.Load();
    private NotifyIcon  _trayIcon  = null!;
    private string _currentBucket = "";
    private string _currentPrefix = "";

    // --- Navigation history ---
    private readonly Stack<(string Bucket, string Prefix)> _navBack    = new();
    private readonly Stack<(string Bucket, string Prefix)> _navForward = new();

    // --- External buckets (added manually, may be owned by other accounts) ---
    private readonly HashSet<string> _externalBuckets = new();

    // --- Browser controls ---
    private ToolStrip toolStrip = null!;
    private ToolStripButton btnConnect = null!;
    private ToolStripButton btnDisconnect = null!;
    private ToolStripButton btnUpload = null!;
    private ToolStripButton btnDownload = null!;
    private ToolStripButton btnDelete = null!;
    private ToolStripButton btnNewFolder = null!;
    private ToolStripButton btnCopyUrl    = null!;
    private ToolStripButton btnRefresh    = null!;
    private ToolStripButton btnNewBucket  = null!;

    private SplitContainer splitOuter = null!;  // top = browser, bottom = transfers
    private SplitContainer splitMain = null!;   // left = buckets, right = files
    private ListBox lstBuckets = null!;
    private FileListView lvFiles = null!;
    private Label    lblPath    = null!;
    private TextBox  txtFilter  = null!;

    // Backing store for search filter — all items in the current folder
    private readonly List<ListViewItem> _allFileItems = new();

    // Column sort state
    private int  _sortColumn    = 0;   // 0=Name, 1=Size, 2=Date
    private bool _sortAscending = true;

    private StatusStrip statusStrip = null!;
    private ToolStripStatusLabel statusLabel = null!;
    private readonly ToolStripStatusLabel _updateLabel = new()
    {
        IsLink    = true,
        Visible   = false,
        Alignment = ToolStripItemAlignment.Right,
        LinkColor = Color.DodgerBlue,
        ToolTipText = "Click to open releases page"
    };

    // --- Transfer panel controls ---
    private FileListView lvTransfers = null!;
    private Button btnPauseJob = null!;
    private Button btnResumeJob = null!;
    private Button btnCancelJob = null!;
    private Button btnClearCompleted = null!;

    private readonly Dictionary<Guid, ListViewItem> _jobItems = new();

    // Column indices in lvTransfers
    private const int TColDir      = 0;
    private const int TColFile     = 1;
    private const int TColStatus   = 2;
    private const int TColProgress = 3;
    private const int TColSize     = 4;
    private const int TColSpeed    = 5;

    // ── Shell file type / icon lookup (SHGetFileInfo) ─────────────────────────
    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    private const uint SHGFI_ICON              = 0x100;
    private const uint SHGFI_SMALLICON         = 0x001;
    private const uint SHGFI_TYPENAME          = 0x400;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x010;
    private const uint FILE_ATTRIBUTE_NORMAL   = 0x080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x010;

    // Shared icon image list — assigned to lvFiles.SmallImageList
    private static readonly ImageList _shellIcons = new()
        { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(16, 16) };
    private static readonly Dictionary<string, int>    _iconIndexCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _typeCache      = new(StringComparer.OrdinalIgnoreCase);
    private static int _nextIconIdx = 0;

    private static void QueryShell(string fakePath, uint fileAttr, out string typeName, out int iconIndex)
    {
        // Type name
        var shfi = new SHFILEINFO();
        SHGetFileInfo(fakePath, fileAttr, ref shfi, (uint)Marshal.SizeOf(shfi),
            SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
        typeName = string.IsNullOrWhiteSpace(shfi.szTypeName) ? "" : shfi.szTypeName;

        // Icon
        shfi = new SHFILEINFO();
        SHGetFileInfo(fakePath, fileAttr, ref shfi, (uint)Marshal.SizeOf(shfi),
            SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
        if (shfi.hIcon != IntPtr.Zero)
        {
            using var icon = Icon.FromHandle(shfi.hIcon);
            _shellIcons.Images.Add(icon.ToBitmap());
            DestroyIcon(shfi.hIcon);
            iconIndex = _nextIconIdx++;
        }
        else { iconIndex = -1; }
    }

    private static (string TypeName, int IconIndex) GetShellInfo(string fileName, bool isFolder)
    {
        string cacheKey = isFolder ? "\x00folder" : Path.GetExtension(fileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(cacheKey)) cacheKey = "\x00noext";

        if (_typeCache.TryGetValue(cacheKey, out var tn) && _iconIndexCache.TryGetValue(cacheKey, out var ii))
            return (tn, ii);

        string fakePath = isFolder ? "folder" : ("file" + cacheKey);
        uint   attr     = isFolder ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        QueryShell(fakePath, attr, out var typeName, out var iconIndex);

        if (isFolder && string.IsNullOrEmpty(typeName)) typeName = "Folder";
        else if (string.IsNullOrEmpty(typeName)) typeName = cacheKey.TrimStart('.').ToUpperInvariant() + " File";

        _typeCache[cacheKey]      = typeName;
        _iconIndexCache[cacheKey] = iconIndex;
        return (typeName, iconIndex);
    }

    public MainForm()
    {
        InitializeComponent();
        SetConnected(false);
    }

    private void InitializeComponent()
    {
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        Text = $"S3 Lite {ver?.Major}.{ver?.Minor}.{ver?.Build}";
        Size = new Size(1100, 720);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        var appIcon = LoadEmbeddedIcon();
        if (appIcon != null) Icon = appIcon;

        bool _isDark = _settings.Theme == "Dark";

        // ── ToolStrip ──────────────────────────────────────────────────────────
        toolStrip = new ToolStrip { ImageScalingSize = new Size(16, 16) };

        btnConnect    = MakeBtn("Connect",    "🔌", ToolStripConnect_Click);
        btnDisconnect = MakeBtn("Disconnect", "⏏",  ToolStripDisconnect_Click);
        btnRefresh    = MakeBtn("Refresh",    "↺",   ToolStripRefresh_Click);
        btnNewBucket  = MakeBtn("New Bucket", "🪣",  ToolStripNewBucket_Click);
        btnUpload     = MakeBtn("Upload",     "⬆",   ToolStripUpload_Click);
        btnDownload   = MakeBtn("Download",   "⬇",   ToolStripDownload_Click);
        btnDelete     = MakeBtn("Delete",     "🗑",   ToolStripDelete_Click);
        btnNewFolder  = MakeBtn("New Folder", "📁",  ToolStripNewFolder_Click);
        btnCopyUrl    = MakeBtn("Web URL",     "🔗",  ToolStripWebUrl_Click);
        var btnCompress = MakeBtn("Compression", "🗜",  (_, _) => BeginInvoke(DoCompressionRules));
        var btnOptions  = MakeBtn("Options",     "⚙",   ToolStripOptions_Click);

        toolStrip.Items.AddRange(new ToolStripItem[]
        {
            btnConnect, btnDisconnect,
            new ToolStripSeparator(),
            btnRefresh, btnNewBucket,
            new ToolStripSeparator(),
            btnUpload, btnDownload,
            new ToolStripSeparator(),
            btnDelete, btnNewFolder,
            new ToolStripSeparator(),
            btnCopyUrl,
            new ToolStripSeparator(),
            btnCompress,
            new ToolStripSeparator(),
            btnOptions
        });

        // ── Status bar ─────────────────────────────────────────────────────────
        statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        statusStrip = new StatusStrip();
        statusStrip.Items.Add(statusLabel);
        statusStrip.Items.Add(_updateLabel);

        // ── Path label ────────────────────────────────────────────────────────
        lblPath = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Padding = new Padding(4, 3, 0, 0),
            BackColor = SystemColors.ControlLight,
            Font = new Font(Font.FontFamily, 8.5f)
        };

        // ── Bucket list ───────────────────────────────────────────────────────
        lstBuckets = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = _isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window };
        lstBuckets.SelectedIndexChanged += LstBuckets_SelectedIndexChanged;
        lstBuckets.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) BeginInvoke(DoDeleteBucket); };

        var ctxBuckets    = new ContextMenuStrip();
        var ctxCopy       = new ToolStripMenuItem("Copy all files to…");
        var ctxMove       = new ToolStripMenuItem("Move all files to…");
        var ctxDlAll      = new ToolStripMenuItem("Download all files to…");
        var ctxDelBkt     = new ToolStripMenuItem("Delete Bucket…");
        var ctxRemoveExt  = new ToolStripMenuItem("Remove from Buckets List");
        var ctxAddExt     = new ToolStripMenuItem("Add External Bucket…");
        var ctxBktProps   = new ToolStripMenuItem("Properties…");

        // ── Change Storage Class submenu ──────────────────────────────────────
        var ctxChangeClass = new ToolStripMenuItem("Change Storage Class to…");
        foreach (var sc in new[] { "STANDARD", "STANDARD_IA", "ONEZONE_IA", "GLACIER", "GLACIER_IR", "DEEP_ARCHIVE", "INTELLIGENT_TIERING" })
        {
            var scItem = new ToolStripMenuItem(sc);
            string captured = sc;
            scItem.Click += (_, _) => BeginInvoke(() => DoChangeBucketStorageClass(captured));
            ctxChangeClass.DropDownItems.Add(scItem);
        }

        ctxCopy.Click      += (_, _) => BeginInvoke(() => DoCopyMoveBucket(move: false));
        ctxMove.Click      += (_, _) => BeginInvoke(() => DoCopyMoveBucket(move: true));
        ctxDlAll.Click     += (_, _) => BeginInvoke(DoDownloadAllFromBucket);
        ctxDelBkt.Click    += CtxDeleteBucket_Click;
        ctxRemoveExt.Click += (_, _) => BeginInvoke(DoRemoveExternalBucket);
        ctxAddExt.Click    += (_, _) => BeginInvoke(DoAddExternalBucket);
        ctxBktProps.Click  += (_, _) => BeginInvoke(DoShowBucketProperties);
        ctxBuckets.Items.AddRange(new ToolStripItem[]
        {
            ctxCopy, ctxMove, ctxDlAll,
            new ToolStripSeparator(),
            ctxChangeClass,
            new ToolStripSeparator(),
            ctxDelBkt, ctxRemoveExt,
            new ToolStripSeparator(),
            ctxAddExt,
            new ToolStripSeparator(),
            ctxBktProps
        });
        ctxBuckets.Opening += (_, _) =>
        {
            bool hasBucket  = lstBuckets.SelectedItem is string;
            bool isExternal = hasBucket && _externalBuckets.Contains((string)lstBuckets.SelectedItem!);
            ctxCopy.Enabled        = hasBucket;
            ctxMove.Enabled        = hasBucket;
            ctxDlAll.Enabled       = hasBucket;
            ctxChangeClass.Enabled = hasBucket;
            ctxDelBkt.Enabled      = hasBucket && !isExternal;
            ctxDelBkt.Visible      = !isExternal;
            ctxRemoveExt.Enabled   = isExternal;
            ctxRemoveExt.Visible   = isExternal;
            ctxAddExt.Enabled      = _s3 != null;
            ctxBktProps.Enabled    = hasBucket;
        };
        lstBuckets.ContextMenuStrip = ctxBuckets;

        var bucketPanel = new Panel { Dock = DockStyle.Fill };
        var bucketHeader = new Label
        {
            Text = " Buckets",
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = SystemColors.ControlDark,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold)
        };
        bucketPanel.Controls.Add(lstBuckets);
        bucketPanel.Controls.Add(bucketHeader);

        // ── File list ─────────────────────────────────────────────────────────
        lvFiles = new FileListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = false,
            MultiSelect = true,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            BackColor = _isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window,
        };
        lvFiles.Columns.Add("Name", 260);
        lvFiles.Columns.Add("Size", 80, HorizontalAlignment.Right);
        lvFiles.Columns.Add("Last Modified", 140);
        lvFiles.Columns.Add("Type", 70);
        lvFiles.Columns.Add("Storage Class", 110);
        lvFiles.SmallImageList = _shellIcons;
        lvFiles.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(SystemColors.Control), e.Bounds);
            // Sort arrow is embedded in header text already
            TextRenderer.DrawText(e.Graphics, e.Header?.Text, e.Font, e.Bounds,
                SystemColors.ControlText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            // Bottom border
            using var p = new Pen(SystemColors.ControlDark, 1);
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        lvFiles.DrawItem    += (_, _) => { };   // all drawing done in DrawSubItem
        lvFiles.DrawSubItem += LvFiles_DrawSubItem;
        lvFiles.DoubleClick   += LvFiles_DoubleClick;
        lvFiles.KeyDown       += LvFiles_KeyDown;
        lvFiles.ColumnClick   += LvFiles_ColumnClick;
        lvFiles.AllowDrop    =  true;
        lvFiles.DragEnter    += LvFiles_DragEnter;
        lvFiles.DragDrop     += LvFiles_DragDrop;

        var ctx = new ContextMenuStrip();
        ctx.Items.Add("New Folder",        null, ToolStripNewFolder_Click);
        ctx.Items.Add(new ToolStripSeparator());
        var ctxPreview = new ToolStripMenuItem("Preview");
        ctxPreview.Click += (_, _) =>
        {
            if (_s3 != null && lvFiles.SelectedItems.Count == 1 && lvFiles.SelectedItems[0].Tag is S3Item f)
                BeginInvoke(() => DoPreviewFile(f));
        };
        ctx.Items.Add(ctxPreview);
        ctx.Items.Add("Download",          null, ToolStripDownload_Click);
        ctx.Items.Add("Delete",            null, ToolStripDelete_Click);
        var ctxRename = new ToolStripMenuItem("Rename…");
        ctxRename.Click += (_, _) =>
        {
            if (lvFiles.SelectedItems.Count == 1 && lvFiles.SelectedItems[0].Tag is S3Item { Type: S3ItemType.Folder })
                BeginInvoke(DoRenameFolder);
            else
                BeginInvoke(DoRenameFile);
        };
        ctx.Items.Add(ctxRename);
        ctx.Items.Add(new ToolStripSeparator());
        ctx.Items.Add("Generate Web URL…", null, ToolStripWebUrl_Click);
        var ctxAcl = new ToolStripMenuItem("Edit Permissions (ACL)…");
        ctxAcl.Click += CtxAcl_Click;
        ctx.Items.Add(ctxAcl);
        var ctxRestore = new ToolStripMenuItem("Restore from Glacier…");
        ctxRestore.Click += (_, _) => BeginInvoke(DoRestoreFromGlacier);
        ctx.Items.Add(ctxRestore);

        // ── Change Storage Class submenu (file-level) ─────────────────────────
        var ctxFileClass = new ToolStripMenuItem("Change Storage Class to…");
        foreach (var sc in new[] { "STANDARD", "STANDARD_IA", "ONEZONE_IA", "GLACIER", "GLACIER_IR", "DEEP_ARCHIVE", "INTELLIGENT_TIERING" })
        {
            var scItem = new ToolStripMenuItem(sc);
            string captured = sc;
            scItem.Click += (_, _) => BeginInvoke(() => DoChangeFileStorageClass(captured));
            ctxFileClass.DropDownItems.Add(scItem);
        }
        ctx.Items.Add(ctxFileClass);
        ctx.Items.Add(new ToolStripSeparator());
        var ctxFileProps = new ToolStripMenuItem("Properties…");
        ctxFileProps.Click += (_, _) =>
        {
            if (_s3 != null && lvFiles.SelectedItems.Count == 1 &&
                lvFiles.SelectedItems[0].Tag is S3Item { Type: S3ItemType.File } fp)
                BeginInvoke(() =>
                {
                    using var dlg = new FilePropertiesForm(_s3, _currentBucket!, fp.Key);
                    dlg.ShowDialog(this);
                });
        };
        ctx.Items.Add(ctxFileProps);

        ctx.Opening += (_, _) =>
        {
            bool anyFile      = lvFiles.SelectedItems.Count > 0 &&
                                lvFiles.SelectedItems.Cast<ListViewItem>().Any(i => i.Tag is S3Item { Type: S3ItemType.File });
            bool singleFile   = lvFiles.SelectedItems.Count == 1 &&
                                lvFiles.SelectedItems[0].Tag is S3Item { Type: S3ItemType.File };
            bool singleFolder = lvFiles.SelectedItems.Count == 1 &&
                                lvFiles.SelectedItems[0].Tag is S3Item { Type: S3ItemType.Folder };
            bool isGlacier    = singleFile && lvFiles.SelectedItems[0].Tag is S3Item si &&
                                (si.StorageClass == "GLACIER" || si.StorageClass == "DEEP_ARCHIVE" || si.StorageClass == "GLACIER_IR");
            ctxPreview.Enabled    = singleFile;
            ctxRename.Enabled     = singleFile || singleFolder;
            ctxAcl.Enabled        = singleFile;
            ctxRestore.Visible    = isGlacier;
            ctxRestore.Enabled    = isGlacier;
            ctxFileClass.Enabled  = anyFile;
            ctxFileProps.Enabled  = singleFile;
        };
        lvFiles.ContextMenuStrip = ctx;

        // ── Filter bar ────────────────────────────────────────────────────────
        txtFilter = new TextBox
        {
            Dock            = DockStyle.Fill,
            PlaceholderText = "🔍  Filter files…",
            BorderStyle     = BorderStyle.None,
        };
        txtFilter.TextChanged += (_, _) => ApplyFilter();

        var filterPanel = new Panel
        {
            Dock      = DockStyle.Top,
            Height    = 24,
            Padding   = new Padding(4, 3, 4, 0),
            BackColor = SystemColors.ControlLight,
        };
        filterPanel.Controls.Add(txtFilter);

        var filesPanel = new Panel { Dock = DockStyle.Fill };
        filesPanel.Controls.Add(lvFiles);
        filesPanel.Controls.Add(filterPanel);  // sits between path label and list
        filesPanel.Controls.Add(lblPath);

        // ── Browser split (left/right) ────────────────────────────────────────
        splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };
        splitMain.Panel1.Controls.Add(bucketPanel);
        splitMain.Panel2.Controls.Add(filesPanel);

        // ── Transfer panel ────────────────────────────────────────────────────
        lvTransfers = new FileListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            BorderStyle = BorderStyle.None,
            OwnerDraw = true,
            BackColor = _isDark ? Color.FromArgb(30, 30, 30) : SystemColors.Window,
        };
        lvTransfers.Columns.Add("",         28);
        lvTransfers.Columns.Add("File",     210);
        lvTransfers.Columns.Add("Status",   72);
        lvTransfers.Columns.Add("Progress", 130);
        lvTransfers.Columns.Add("Size",     145);
        lvTransfers.Columns.Add("Speed",    90);
        lvTransfers.ForeColor        =  SystemColors.ControlText;
        lvTransfers.DrawColumnHeader += (_, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(SystemColors.Control), e.Bounds);
            var padded = Rectangle.FromLTRB(e.Bounds.Left + 4, e.Bounds.Top, e.Bounds.Right, e.Bounds.Bottom);
            TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", lvTransfers.Font, padded,
                SystemColors.ControlText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            using var p = new Pen(SystemColors.ControlDark, 1);
            e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        };
        lvTransfers.DrawItem += (_, _) => { };
        lvTransfers.DrawSubItem      += LvTransfers_DrawSubItem;
        lvTransfers.SelectedIndexChanged += (_, _) => UpdateTransferButtons();

        var ctxTransfers = new ContextMenuStrip();
        ctxTransfers.Items.Add("Pause",   null, (_, _) => PauseSelected());
        ctxTransfers.Items.Add("Resume",  null, (_, _) => ResumeSelected());
        ctxTransfers.Items.Add("Cancel",  null, (_, _) => CancelSelected());
        ctxTransfers.Items.Add(new ToolStripSeparator());
        ctxTransfers.Items.Add("Clear Completed", null, (_, _) => ClearCompleted());
        lvTransfers.ContextMenuStrip = ctxTransfers;

        // Transfer action buttons
        btnPauseJob       = new Button { Text = "⏸ Pause",   Width = 80,  Height = 26 };
        btnResumeJob      = new Button { Text = "▶ Resume",  Width = 80,  Height = 26 };
        btnCancelJob      = new Button { Text = "✖ Cancel",  Width = 80,  Height = 26 };
        btnClearCompleted = new Button { Text = "Clear Done", Width = 88, Height = 26 };
        btnPauseJob.Click       += (_, _) => PauseSelected();
        btnResumeJob.Click      += (_, _) => ResumeSelected();
        btnCancelJob.Click      += (_, _) => CancelSelected();
        btnClearCompleted.Click += (_, _) => ClearCompleted();

        var pnlTransferActions = new Panel { Dock = DockStyle.Bottom, Height = 32 };
        btnPauseJob.Left   = 4;   btnPauseJob.Top   = 3;
        btnResumeJob.Left  = 88;  btnResumeJob.Top  = 3;
        btnCancelJob.Left  = 172; btnCancelJob.Top  = 3;
        btnClearCompleted.Left = 270; btnClearCompleted.Top = 3;
        pnlTransferActions.Controls.AddRange(new Control[] { btnPauseJob, btnResumeJob, btnCancelJob, btnClearCompleted });

        var lblTransferHeader = new Label
        {
            Text = " Transfers",
            Dock = DockStyle.Top,
            Height = 22,
            BackColor = SystemColors.ControlDark,
            ForeColor = Color.White,
            Font = new Font(Font.FontFamily, 8.5f, FontStyle.Bold)
        };

        var pnlTransfers = new Panel { Dock = DockStyle.Fill };
        pnlTransfers.Controls.Add(lvTransfers);
        pnlTransfers.Controls.Add(pnlTransferActions);
        pnlTransfers.Controls.Add(lblTransferHeader);

        // ── Outer split (top = browser, bottom = transfers) ───────────────────
        splitOuter = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            BorderStyle = BorderStyle.None,
        };
        splitOuter.Panel1.Controls.Add(splitMain);
        splitOuter.Panel2.Controls.Add(pnlTransfers);

        Controls.Add(splitOuter);
        Controls.Add(toolStrip);
        Controls.Add(statusStrip);

        Load += (_, _) =>
        {
            splitMain.Panel1MinSize = 120;
            splitMain.Panel2MinSize = 300;
            splitMain.SplitterDistance = 180;

            splitOuter.Panel1MinSize = 180;
            splitOuter.Panel2MinSize = 100;
            splitOuter.SplitterDistance = Math.Max(180, splitOuter.Height - 190);
            splitOuter.Panel2Collapsed = true; // hidden until first transfer
        };

        Load        += (_, _) => Application.AddMessageFilter(_mouseNavFilter = new MouseNavFilter(this));
        Load        += (_, _) => _ = AutoConnectAsync();
        Load        += (_, _) => SetupTrayIcon();
        Load        += (_, _) => _ = CheckForUpdateAsync();
        Resize      += MainForm_Resize;
        FormClosing += MainForm_FormClosing;
    }

    // ── OwnerDraw file rows ───────────────────────────────────────────────────
    private void LvFiles_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        // Phantom rows (no tag) — erase any artifacts and stop
        if (e.Item?.Tag == null)
        {
            e.Graphics.FillRectangle(new SolidBrush(lvFiles.BackColor), e.Bounds);
            return;
        }
        var r   = e.Bounds;
        bool sel = e.Item.Selected;

        // Background
        Color back = sel ? SystemColors.Highlight : lvFiles.BackColor;
        using (var br = new SolidBrush(back))
            e.Graphics.FillRectangle(br, r);

        // Icon + text offset for Name column
        int textX = r.X + 2;
        if (e.ColumnIndex == 0)
        {
            int imgIdx = e.Item.ImageIndex;
            if (imgIdx >= 0 && lvFiles.SmallImageList?.Images.Count > imgIdx)
            {
                int iconY = r.Y + (r.Height - 16) / 2;
                e.Graphics.DrawImage(lvFiles.SmallImageList.Images[imgIdx], r.X + 2, iconY, 16, 16);
            }
            textX = r.X + 20;
        }

        // Text colour — respect per-item colour (e.g. CornflowerBlue for folders)
        Color fore = sel ? SystemColors.HighlightText : e.Item.ForeColor;

        var align = lvFiles.Columns[e.ColumnIndex].TextAlign switch
        {
            HorizontalAlignment.Right  => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _                          => TextFormatFlags.Left
        };
        var textRect = new Rectangle(textX, r.Y, r.Right - textX - 2, r.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text, e.Item.Font ?? lvFiles.Font,
            textRect, fore, align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Grid lines — horizontal bottom + vertical right
        Color line = _settings.Theme == "Dark" ? Color.FromArgb(55, 55, 55) : Color.FromArgb(210, 210, 210);
        using var pen = new Pen(line, 1);
        e.Graphics.DrawLine(pen, r.Left,    r.Bottom - 1, r.Right,    r.Bottom - 1); // horizontal
        e.Graphics.DrawLine(pen, r.Right - 1, r.Top,     r.Right - 1, r.Bottom);     // vertical
    }

    // ── OwnerDraw transfer rows ───────────────────────────────────────────────
    private void LvTransfers_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var g   = e.Graphics;
        bool sel = (e.ItemState & ListViewItemStates.Selected) != 0;

        // Windows extends the last column's bounds to the right edge of the control.
        // Clip every cell to the sum of all defined column widths so nothing draws past that.
        int totalW   = lvTransfers.Columns.Cast<ColumnHeader>().Sum(c => c.Width);
        int cellRight = Math.Min(e.Bounds.Right, totalW);
        var r = new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(0, cellRight - e.Bounds.X), e.Bounds.Height);

        // Fill overflow area (past last column) with BackColor
        if (e.Bounds.Right > cellRight)
            g.FillRectangle(new SolidBrush(lvTransfers.BackColor),
                cellRight, e.Bounds.Y, e.Bounds.Right - cellRight, e.Bounds.Height);

        // Background
        Color back = sel ? SystemColors.Highlight : lvTransfers.BackColor;
        g.FillRectangle(new SolidBrush(back), r);

        Color fore = sel ? SystemColors.HighlightText : (e.Item?.ForeColor ?? lvTransfers.ForeColor);

        if (e.ColumnIndex == TColProgress)
        {
            var job = e.Item?.Tag as TransferJob;

            if (job != null && job.TotalBytes > 0 && job.Status != TransferStatus.Pending)
            {
                int barW = (int)((r.Width - 4) * job.Progress / 100.0);
                if (barW > 0)
                {
                    Color barColor = job.Status switch
                    {
                        TransferStatus.Completed => Color.SeaGreen,
                        TransferStatus.Failed    => Color.IndianRed,
                        TransferStatus.Paused    => Color.SandyBrown,
                        TransferStatus.Cancelled => Color.Silver,
                        _ => Color.SteelBlue
                    };
                    using var brush = new SolidBrush(barColor);
                    g.FillRectangle(brush, r.X + 2, r.Y + 2, barW, r.Height - 4);
                }
            }

            string text;
            if (job == null)
                text = "";
            else if (job.Status == TransferStatus.Pending)
                text = "Queued";
            else if (job.Status == TransferStatus.Running && job.TotalParts > 1 && job.ActiveParts > 0)
                text = $"{job.Progress}%  ·  {job.ActiveParts} parts";
            else
                text = $"{job.Progress}%";

            TextRenderer.DrawText(g, text, lvTransfers.Font, r, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
        else
        {
            string text = e.SubItem?.Text ?? "";
            var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var padded = Rectangle.FromLTRB(r.Left + 3, r.Top, r.Right, r.Bottom);
            TextRenderer.DrawText(g, text, lvTransfers.Font, padded, fore, flags);
        }

        // Grid lines — r is clipped so lines stop at the actual column boundary
        bool isLast = e.ColumnIndex == lvTransfers.Columns.Count - 1;
        Color line = _settings.Theme == "Dark" ? Color.FromArgb(42, 42, 42) : Color.FromArgb(225, 225, 225);
        using var pen = new Pen(line, 1);
        g.DrawLine(pen, r.Left, r.Bottom - 1, r.Right, r.Bottom - 1); // horizontal
        if (!isLast)
            g.DrawLine(pen, r.Right - 1, r.Top, r.Right - 1, r.Bottom); // vertical (skip on last col)
    }

    // ── Factory ───────────────────────────────────────────────────────────────
    private static ToolStripButton MakeBtn(string text, string emoji, EventHandler handler)
    {
        var btn = new ToolStripButton($"{emoji} {text}") { DisplayStyle = ToolStripItemDisplayStyle.Text };
        btn.Click += handler;
        return btn;
    }

    // ── Connection ────────────────────────────────────────────────────────────
    private async Task AutoConnectAsync()
    {
        if (string.IsNullOrEmpty(_settings.LastProfile)) return;
        var profile = ProfileStore.Load().FirstOrDefault(p => p.ProfileName == _settings.LastProfile);
        if (profile == null) return;

        _connection = profile;
        try
        {
            _s3 = new S3Service(_connection);
            _transferManager = new TransferManager(_s3, _settings.MaxConcurrentUploads, _settings.MaxConcurrentDownloads, _settings.ParallelPartsPerUpload);
            _s3.MultipartThresholdBytes = (long)_settings.MultipartThresholdMB * 1024 * 1024;
            _s3.CompressionRules        = CompressionRuleStore.Load();
            _s3.ListPageSize            = _settings.EnablePagination ? _settings.PageSize : null;
            _transferManager.JobAdded   += OnJobAdded;
            _transferManager.JobChanged += OnJobChanged;

            foreach (var kvp in _settings.ExternalBuckets)
            {
                _externalBuckets.Add(kvp.Key);
                _s3.RegisterExternalBucket(kvp.Key, kvp.Value);
            }

            SetConnected(true);
            SetStatus($"Connected as {_connection.ProfileName}");
            await LoadBucketsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Auto-connect failed: {ex.Message}");
        }
    }

    private void ToolStripConnect_Click(object? sender, EventArgs e)
    {
        using var dlg = new ConnectForm(_connection);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        _s3?.Dispose();
        _transferManager?.Dispose();
        _connection = dlg.Result;
        try
        {
            _s3 = new S3Service(_connection);
            _transferManager = new TransferManager(_s3, _settings.MaxConcurrentUploads, _settings.MaxConcurrentDownloads, _settings.ParallelPartsPerUpload);
            _s3.MultipartThresholdBytes = (long)_settings.MultipartThresholdMB * 1024 * 1024;
            _s3.CompressionRules        = CompressionRuleStore.Load();
            _s3.ListPageSize            = _settings.EnablePagination ? _settings.PageSize : null;
            _transferManager.JobAdded   += OnJobAdded;
            _transferManager.JobChanged += OnJobChanged;

            // Register any saved external buckets with the new service
            _externalBuckets.Clear();
            foreach (var kvp in _settings.ExternalBuckets)
            {
                _externalBuckets.Add(kvp.Key);
                _s3.RegisterExternalBucket(kvp.Key, kvp.Value);
            }

            _settings.LastProfile = _connection.ProfileName;
            SettingsStore.Save(_settings);
            SetConnected(true);
            SetStatus($"Connected as {_connection.ProfileName}");
            _ = LoadBucketsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Connection failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToolStripDisconnect_Click(object? sender, EventArgs e)
    {
        _transferManager?.Dispose(); _transferManager = null;
        _s3?.Dispose(); _s3 = null;
        _externalBuckets.Clear();
        lstBuckets.Items.Clear();
        lvFiles.Items.Clear();
        _currentBucket = ""; _currentPrefix = "";
        UpdatePathLabel();
        _settings.LastProfile = null;
        SettingsStore.Save(_settings);
        SetConnected(false);
        SetStatus("Disconnected");
    }

    private static Icon? LoadEmbeddedIcon()
    {
        var asm  = System.Reflection.Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("app.ico"));
        if (name == null) return null;
        using var stream = asm.GetManifestResourceStream(name);
        return stream == null ? null : new Icon(stream);
    }

    private void SetupTrayIcon()
    {
        var trayIcon = new NotifyIcon
        {
            Text    = "S3 Lite",
            Icon    = LoadEmbeddedIcon() ?? SystemIcons.Application,
            Visible = _settings.ShowTrayIcon
        };

        var ctxTray = new ContextMenuStrip();
        ctxTray.Items.Add("Open", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        ctxTray.Items.Add(new ToolStripSeparator());
        ctxTray.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; Application.Exit(); });
        trayIcon.ContextMenuStrip = ctxTray;
        trayIcon.DoubleClick     += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };

        _trayIcon = trayIcon;
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
            _trayIcon.Visible = true;
            _trayIcon.ShowBalloonTip(1500, "S3 Lite", "S3 Lite is running in the background.", ToolTipIcon.Info);
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_mouseNavFilter != null) Application.RemoveMessageFilter(_mouseNavFilter);
        _trayIcon?.Dispose();
        _transferManager?.Dispose();
        _s3?.Dispose();
    }

    private void ToolStripRefresh_Click(object? sender, EventArgs e) => _ = RefreshCurrentAsync();

    private void DoCompressionRules()
    {
        using var dlg = new CompressionRulesForm();
        dlg.ShowDialog(this);
        // Reload rules so uploads immediately use any changes
        if (_s3 != null)
            _s3.CompressionRules = CompressionRuleStore.Load();
    }

    private void ToolStripOptions_Click(object? sender, EventArgs e) => BeginInvoke(DoOptions);
    private void DoOptions()
    {
        using var dlg = new OptionsForm(_settings);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool themeChanged = dlg.Result.Theme != _settings.Theme;
        _settings = dlg.Result;
        SettingsStore.Save(_settings);
        SetStatus("Settings saved. New limits apply to the next connection.");

        // Apply tray changes immediately
        if (_trayIcon != null)
            _trayIcon.Visible = _settings.ShowTrayIcon;

        if (themeChanged)
        {
            var restart = MessageBox.Show(
                "Theme change requires a restart to take full effect.\r\nRestart S3 Lite now?",
                "Restart Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1);
            if (restart == DialogResult.Yes)
                Application.Restart();
        }
    }

    // ── Bucket / file loading ─────────────────────────────────────────────────
    private async Task LoadBucketsAsync()
    {
        if (_s3 == null) return;
        try
        {
            SetStatus("Loading buckets…");
            var buckets = await _s3.ListBucketsAsync();
            lstBuckets.Items.Clear();
            foreach (var b in buckets) lstBuckets.Items.Add(b);

            // Append external buckets that aren't already in the owned list
            foreach (var ext in _externalBuckets)
                if (!lstBuckets.Items.Contains(ext))
                    lstBuckets.Items.Add(ext);

            int extCount = _externalBuckets.Count;
            SetStatus(extCount > 0
                ? $"{buckets.Count} bucket(s) + {extCount} external"
                : $"{buckets.Count} bucket(s)");
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private async void LstBuckets_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstBuckets.SelectedItem is not string bucket) return;
        await NavigateToAsync(bucket, "");
    }

    private async Task LoadFilesAsync()
    {
        if (_s3 == null || string.IsNullOrEmpty(_currentBucket)) return;
        SetStatus("Loading…");
        lvFiles.Items.Clear();
        _allFileItems.Clear();
        UpdatePathLabel();

        try
        {
            if (!string.IsNullOrEmpty(_currentPrefix))
            {
                var (_, upIcon) = GetShellInfo("", true);
                var up = new ListViewItem(".. (up)", upIcon) { Tag = "..", ForeColor = SystemColors.GrayText };
                up.SubItems.Add(""); up.SubItems.Add(""); up.SubItems.Add(""); up.SubItems.Add("");
                lvFiles.Items.Add(up);
            }

            var items = await _s3.ListObjectsAsync(_currentBucket, _currentPrefix);
            foreach (var item in items)
            {
                var (typeName, iconIdx) = GetShellInfo(item.Name, item.Type == S3ItemType.Folder);
                var lvi = new ListViewItem(item.Type == S3ItemType.Folder ? $"[{item.Name}]" : item.Name, iconIdx)
                {
                    Tag = item,
                    ForeColor = item.Type == S3ItemType.Folder ? Color.CornflowerBlue : SystemColors.ControlText
                };
                lvi.SubItems.Add(item.DisplaySize);
                lvi.SubItems.Add(item.Type == S3ItemType.File ? item.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "");
                lvi.SubItems.Add(item.Type == S3ItemType.File ? typeName : "");
                lvi.SubItems.Add(item.Type == S3ItemType.File ? item.StorageClass : "");
                lvFiles.Items.Add(lvi);
            }

            var fileCount   = items.Count(i => i.Type == S3ItemType.File);
            var folderCount = items.Count(i => i.Type == S3ItemType.Folder);
            SetStatus($"{folderCount} folder(s), {fileCount} file(s)");

            // Save all items for filtering, then apply any active filter
            _allFileItems.AddRange(lvFiles.Items.Cast<ListViewItem>());
            if (!string.IsNullOrEmpty(txtFilter.Text))
                ApplyFilter();
        }
        catch (Exception ex) { SetStatus($"Error: {ex.Message}"); }
    }

    private void ApplyFilter()
    {
        string filter = txtFilter.Text.Trim();
        lvFiles.BeginUpdate();
        lvFiles.Items.Clear();
        if (string.IsNullOrEmpty(filter))
        {
            lvFiles.Items.AddRange(_allFileItems.ToArray());
        }
        else
        {
            var matches = _allFileItems
                .Where(i => i.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            lvFiles.Items.AddRange(matches);
        }
        lvFiles.EndUpdate();
    }

    private void LvFiles_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (_sortColumn == e.Column)
            _sortAscending = !_sortAscending;
        else
        {
            _sortColumn    = e.Column;
            _sortAscending = true;
        }

        // Update column header arrows
        string[] headers = ["Name", "Size", "Last Modified", "Type", "Storage Class"];
        for (int i = 0; i < lvFiles.Columns.Count; i++)
            lvFiles.Columns[i].Text = headers[i];
        string arrow = _sortAscending ? " ▲" : " ▼";
        lvFiles.Columns[_sortColumn].Text += arrow;

        SortFileItems();
        ApplyFilter();
    }

    private void SortFileItems()
    {
        // Pin ".. (up)" at top, then folders before files, then sort within each group
        _allFileItems.Sort((a, b) =>
        {
            // ".. (up)" always first
            bool aUp = a.Tag is string;
            bool bUp = b.Tag is string;
            if (aUp || bUp) return aUp ? -1 : 1;

            var ai = a.Tag as S3Item;
            var bi = b.Tag as S3Item;
            if (ai == null || bi == null) return 0;

            // Folders before files
            if (ai.Type != bi.Type)
                return ai.Type == S3ItemType.Folder ? -1 : 1;

            int cmp = _sortColumn switch
            {
                1 => ai.Size.CompareTo(bi.Size),
                2 => ai.LastModified.CompareTo(bi.LastModified),
                3 => ai.Type.CompareTo(bi.Type),
                4 => string.Compare(ai.StorageClass, bi.StorageClass, StringComparison.OrdinalIgnoreCase),
                _ => string.Compare(ai.Name, bi.Name, StringComparison.OrdinalIgnoreCase)
            };
            return _sortAscending ? cmp : -cmp;
        });
    }

    private async void LvFiles_DoubleClick(object? sender, EventArgs e)
    {
        if (lvFiles.SelectedItems.Count == 0) return;
        var lvi = lvFiles.SelectedItems[0];

        if (lvi.Tag is string)
        {
            await NavigateUpAsync();
        }
        else if (lvi.Tag is S3Item item && item.Type == S3ItemType.Folder)
        {
            await NavigateToAsync(_currentBucket, item.Key);
        }
        else if (lvi.Tag is S3Item file && file.Type == S3ItemType.File && _s3 != null)
        {
            BeginInvoke(() => DoPreviewFile(file));
        }
    }

    private void LvFiles_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)                    ToolStripDelete_Click(sender, e);
        if (e.KeyCode == Keys.Back)                      _ = NavigateUpAsync();
        if (e.KeyCode == Keys.Return)                    { e.Handled = true; e.SuppressKeyPress = true; LvFiles_DoubleClick(sender, e); }
        if (e.KeyCode == Keys.A     && e.Control)        { lvFiles.Items.Cast<ListViewItem>().ToList().ForEach(i => i.Selected = true); e.Handled = true; }
        if (e.KeyCode == Keys.U     && e.Control)        { e.Handled = true; BeginInvoke(ToolStripUpload_Click, this, EventArgs.Empty); }
    }

    // ── Mouse back/forward buttons ────────────────────────────────────────────
    // WndProc only fires when the form itself has focus; use IMessageFilter so
    // XButton messages are caught even when a child control (e.g. lvFiles) is focused.
    private sealed class MouseNavFilter : IMessageFilter
    {
        private const int WM_XBUTTONDOWN = 0x020B;
        private readonly MainForm _owner;
        public MouseNavFilter(MainForm owner) => _owner = owner;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg == WM_XBUTTONDOWN)
            {
                int button = (int)(m.WParam.ToInt64() >> 16) & 0xFFFF;
                if (button == 1) { _ = _owner.NavigateBackAsync();    return true; }
                if (button == 2) { _ = _owner.NavigateForwardAsync(); return true; }
            }
            return false;
        }
    }

    private MouseNavFilter? _mouseNavFilter;

    private void LvFiles_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = _transferManager != null &&
                   !string.IsNullOrEmpty(_currentBucket) &&
                   e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void LvFiles_DragDrop(object? sender, DragEventArgs e)
    {
        if (_transferManager == null || string.IsNullOrEmpty(_currentBucket)) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths) return;

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                _transferManager.Enqueue(TransferDirection.Upload, _currentBucket,
                    _currentPrefix + Path.GetFileName(path), path);
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    var relative = Path.GetRelativePath(Path.GetDirectoryName(path)!, file).Replace('\\', '/');
                    _transferManager.Enqueue(TransferDirection.Upload, _currentBucket,
                        _currentPrefix + Path.GetFileName(path) + "/" + relative, file);
                }
            }
        }
    }

    /// <summary>Navigate to a bucket/prefix, pushing current location onto the back stack.</summary>
    private async Task NavigateToAsync(string bucket, string prefix)
    {
        if (bucket == _currentBucket && prefix == _currentPrefix) return;
        _navBack.Push((_currentBucket, _currentPrefix));
        _navForward.Clear();
        _currentBucket = bucket;
        _currentPrefix = prefix;
        txtFilter.Text = "";   // clear search on every navigation
        await LoadFilesAsync();
    }

    internal async Task NavigateBackAsync()
    {
        if (_navBack.Count == 0) return;
        _navForward.Push((_currentBucket, _currentPrefix));
        (_currentBucket, _currentPrefix) = _navBack.Pop();
        await LoadFilesAsync();
    }

    internal async Task NavigateForwardAsync()
    {
        if (_navForward.Count == 0) return;
        _navBack.Push((_currentBucket, _currentPrefix));
        (_currentBucket, _currentPrefix) = _navForward.Pop();
        await LoadFilesAsync();
    }

    private async Task NavigateUpAsync()
    {
        if (string.IsNullOrEmpty(_currentPrefix)) return;
        var parts = _currentPrefix.TrimEnd('/').Split('/');
        string parent = parts.Length <= 1 ? "" : string.Join("/", parts[..^1]) + "/";
        await NavigateToAsync(_currentBucket, parent);
    }

    private async Task RefreshCurrentAsync()
    {
        if (!string.IsNullOrEmpty(_currentBucket)) await LoadFilesAsync();
        else await LoadBucketsAsync();
    }

    // ── Upload ────────────────────────────────────────────────────────────────
    private void ToolStripUpload_Click(object? sender, EventArgs e) => BeginInvoke(DoUpload);

    private void DoUpload()
    {
        if (_transferManager == null || string.IsNullOrEmpty(_currentBucket)) return;
        using var dlg = new OpenFileDialog { Multiselect = true, Title = "Select files to upload" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var file in dlg.FileNames)
        {
            var key = _currentPrefix + Path.GetFileName(file);
            _transferManager.Enqueue(TransferDirection.Upload, _currentBucket, key, file);
        }
    }

    // ── Download ──────────────────────────────────────────────────────────────
    private void ToolStripDownload_Click(object? sender, EventArgs e) => BeginInvoke(DoDownload);

    private void DoDownload()
    {
        if (_transferManager == null || lvFiles.SelectedItems.Count == 0) return;

        var files = lvFiles.SelectedItems.Cast<ListViewItem>()
            .Where(i => i.Tag is S3Item { Type: S3ItemType.File })
            .Select(i => (S3Item)i.Tag!)
            .ToList();

        if (files.Count == 0)
        {
            MessageBox.Show("Select file(s) to download.", "Download", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var destFolder  = FolderPicker.Pick(Handle, defaultPath);
        if (string.IsNullOrWhiteSpace(destFolder)) return;

        foreach (var item in files)
        {
            var localPath = Path.Combine(destFolder, item.Name);
            _transferManager.Enqueue(TransferDirection.Download, _currentBucket, item.Key, localPath);
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────
    private void ToolStripDelete_Click(object? sender, EventArgs e) => BeginInvoke(DoDelete);

    private async void DoDelete()
    {
        if (_s3 == null || lvFiles.SelectedItems.Count == 0) return;

        var selected = lvFiles.SelectedItems.Cast<ListViewItem>()
            .Where(i => i.Tag is S3Item)
            .Select(i => (S3Item)i.Tag!)
            .ToList();

        if (selected.Count == 0) return;
        if (MessageBox.Show($"Delete {selected.Count} item(s)?", "Confirm Delete",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        foreach (var item in selected)
        {
            try
            {
                if (item.Type == S3ItemType.Folder)
                    await _s3.DeleteFolderAsync(_currentBucket, item.Key);
                else
                    await _s3.DeleteObjectAsync(_currentBucket, item.Key);
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Delete Error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        await LoadFilesAsync();
    }

    // ── Rename ────────────────────────────────────────────────────────────────
    private async void DoRenameFile()
    {
        if (_s3 == null) return;
        if (lvFiles.SelectedItems.Count != 1) return;
        if (lvFiles.SelectedItems[0].Tag is not S3Item { Type: S3ItemType.File } item) return;

        string newName = InputPrompt("New file name:", "Rename", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        // Keep the same prefix, only change the filename
        string newKey = _currentPrefix + newName.Trim();
        try
        {
            await _s3.RenameObjectAsync(_currentBucket, item.Key, newKey);
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Rename Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── File preview ─────────────────────────────────────────────────────────
    private void DoPreviewFile(S3Item item)
    {
        if (_s3 == null) return;
        int previewMB = _settings.LimitPreviewSize ? _settings.PreviewMaxSizeMB : 500;
        var dlg = new FilePreviewForm(_s3, _currentBucket, item.Key, previewMB);
        dlg.Show(this);
    }

    // ── Rename folder ─────────────────────────────────────────────────────────
    private async void DoRenameFolder()
    {
        if (_s3 == null) return;
        if (lvFiles.SelectedItems.Count != 1) return;
        if (lvFiles.SelectedItems[0].Tag is not S3Item { Type: S3ItemType.Folder } folder) return;

        // Display name is "[FolderName]" — strip brackets
        string oldName = folder.Name.TrimEnd('/');
        string newName = InputPrompt("New folder name:", "Rename Folder", oldName);
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;

        string oldPrefix = _currentPrefix + oldName;
        string newPrefix = _currentPrefix + newName.Trim();
        try
        {
            SetStatus($"Renaming folder…");
            await _s3.RenameFolderAsync(_currentBucket, oldPrefix, newPrefix);
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Rename Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Restore from Glacier ──────────────────────────────────────────────────
    private async void DoRestoreFromGlacier()
    {
        if (_s3 == null) return;
        if (lvFiles.SelectedItems.Count != 1) return;
        if (lvFiles.SelectedItems[0].Tag is not S3Item { Type: S3ItemType.File } item) return;

        string daysStr = InputPrompt("Number of days to keep the restored copy available:", "Restore from Glacier", "7");
        if (!int.TryParse(daysStr, out int days) || days < 1) return;

        try
        {
            SetStatus($"Requesting restore for {item.Name}…");
            await _s3.RestoreObjectAsync(_currentBucket, item.Key, days);
            SetStatus($"Restore requested for {item.Name}. It may take several hours to become available.");
            MessageBox.Show(
                $"Restore request submitted for '{item.Name}'.\n\nThe file will be available within a few hours (Standard tier). It will remain accessible for {days} day(s).",
                "Restore Requested", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Restore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── New Folder ────────────────────────────────────────────────────────────
    private void ToolStripNewFolder_Click(object? sender, EventArgs e) => BeginInvoke(DoNewFolder);

    private async void DoNewFolder()
    {
        if (_s3 == null || string.IsNullOrEmpty(_currentBucket)) return;
        var name = InputPrompt("Folder name:", "New Folder");
        if (string.IsNullOrWhiteSpace(name)) return;
        await _s3.CreateFolderAsync(_currentBucket, _currentPrefix + name.Trim());
        await LoadFilesAsync();
    }

    // ── New Bucket ────────────────────────────────────────────────────────────
    private void ToolStripNewBucket_Click(object? sender, EventArgs e) => BeginInvoke(DoNewBucket);

    private async void DoNewBucket()
    {
        if (_s3 == null) return;
        string defaultRegion = _connection?.Region ?? "us-east-1";

        using var dlg = new CreateBucketForm(defaultRegion);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var opts = new CreateBucketOptions(
            BlockPublicAccess : dlg.BlockPublicAccess,
            DisableAcls       : dlg.DisableAcls,
            ObjectLock        : dlg.ObjectLock,
            LockMode          : dlg.LockResult.Mode,
            LockDays          : dlg.LockResult.Days,
            LockYears         : dlg.LockResult.Years
        );

        SetStatus($"Creating bucket '{dlg.BucketName}'…");
        try
        {
            await _s3.CreateBucketAsync(dlg.BucketName, dlg.BucketRegion, opts);
            SetStatus($"Bucket '{dlg.BucketName}' created.");
            await LoadBucketsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Create Bucket Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Change storage class for selected files ───────────────────────────────
    private async void DoChangeFileStorageClass(string storageClass)
    {
        if (_s3 == null) return;
        var files = lvFiles.SelectedItems.Cast<ListViewItem>()
            .Where(i => i.Tag is S3Item { Type: S3ItemType.File })
            .Select(i => (S3Item)i.Tag!)
            .ToList();
        if (files.Count == 0) return;

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                SetStatus($"Changing storage class… {i + 1}/{files.Count}");
                await _s3.ChangeObjectStorageClassAsync(_currentBucket, files[i].Key, storageClass);
                files[i].StorageClass = storageClass;
            }
            SetStatus($"Storage class changed to {storageClass} for {files.Count} file(s).");
            await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Change storage class for all objects in bucket ───────────────────────
    private async void DoChangeBucketStorageClass(string storageClass)
    {
        if (_s3 == null || lstBuckets.SelectedItem is not string bucket) return;

        var result = MessageBox.Show(
            $"Change the storage class of all objects in '{bucket}' to {storageClass}?\n\nThis performs a server-side copy on every file.",
            "Change Storage Class", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (result != DialogResult.OK) return;

        SetStatus($"Changing storage class to {storageClass}…");
        btnRefresh.Enabled = false;
        try
        {
            var progress = new Progress<(int done, int total)>(p =>
                SetStatus($"Changing storage class… {p.done}/{p.total}"));
            await _s3.ChangeStorageClassAsync(bucket, storageClass, progress);
            SetStatus($"Storage class changed to {storageClass}.");
            if (_currentBucket == bucket)
                await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { btnRefresh.Enabled = true; }
    }

    // ── Bucket properties ─────────────────────────────────────────────────────
    private void DoShowBucketProperties()
    {
        if (_s3 == null || lstBuckets.SelectedItem is not string bucket) return;
        using var dlg = new BucketPropertiesForm(_s3, bucket);
        dlg.ShowDialog(this);
    }

    // ── Add / Remove external buckets ────────────────────────────────────────
    private void DoAddExternalBucket()
    {
        if (_s3 == null) return;
        using var dlg = new AddExternalBucketForm(_s3);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string name   = dlg.BucketName;
        string region = dlg.BucketRegion;

        if (_externalBuckets.Contains(name) || lstBuckets.Items.Contains(name))
        {
            MessageBox.Show($"'{name}' is already in the bucket list.", "Add External Bucket",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _externalBuckets.Add(name);
        _settings.ExternalBuckets[name] = region;
        SettingsStore.Save(_settings);
        _s3.RegisterExternalBucket(name, region);
        lstBuckets.Items.Add(name);
        lstBuckets.SelectedItem = name;
        SetStatus($"External bucket '{name}' added ({region}).");
    }

    private void DoRemoveExternalBucket()
    {
        if (lstBuckets.SelectedItem is not string bucket) return;
        if (!_externalBuckets.Contains(bucket)) return;

        var confirm = MessageBox.Show(
            $"Remove '{bucket}' from the bucket list?\n\nThis only removes it from your local list — the bucket itself is not affected.",
            "Remove External Bucket",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _externalBuckets.Remove(bucket);
        _settings.ExternalBuckets.Remove(bucket);
        SettingsStore.Save(_settings);
        lstBuckets.Items.Remove(bucket);
        if (_currentBucket == bucket)
        {
            _currentBucket = ""; _currentPrefix = "";
            lvFiles.Items.Clear();
            UpdatePathLabel();
        }
        SetStatus($"External bucket '{bucket}' removed from list.");
    }

    // ── Copy / Move all files in bucket ──────────────────────────────────────
    private async void DoCopyMoveBucket(bool move)
    {
        if (_s3 == null || lstBuckets.SelectedItem is not string srcBucket) return;

        string infoText = move
            ? $"All files in '{srcBucket}' will be moved to the selected destination and removed from the source."
            : $"All files in '{srcBucket}' will be copied to the selected destination. The source will not be changed.";
        using var dlg = new S3BrowseForm(_s3, infoText);
        dlg.Text = move ? "Move all files to…" : "Copy all files to…";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string dstBucket = dlg.SelectedBucket;
        string dstPrefix = dlg.SelectedPrefix;

        if (dstBucket == srcBucket && string.IsNullOrEmpty(dstPrefix))
        {
            MessageBox.Show("Source and destination are the same.", "Copy/Move",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string verb = move ? "Moving" : "Copying";
        SetStatus($"{verb} all files from '{srcBucket}' to '{dstBucket}/{dstPrefix}'…");

        var progress = new Progress<(int done, int total, string key)>(r =>
            SetStatus($"{verb}… {r.done}/{r.total} — {Path.GetFileName(r.key)}"));

        try
        {
            await _s3.CopyPrefixAsync(srcBucket, "", dstBucket, dstPrefix, move, progress);
            string action = move ? "Moved" : "Copied";
            SetStatus($"{action} all files from '{srcBucket}' to '{dstBucket}/{dstPrefix}'.");
            if (move && _currentBucket == srcBucket) await LoadFilesAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Copy/Move Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Download all files in bucket ──────────────────────────────────────────
    private async void DoDownloadAllFromBucket()
    {
        if (_s3 == null || _transferManager == null || lstBuckets.SelectedItem is not string bucket) return;

        var destFolder = FolderPicker.Pick(Handle, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
        if (string.IsNullOrWhiteSpace(destFolder)) return;

        SetStatus($"Enumerating files in '{bucket}'…");
        try
        {
            var items = await _s3.ListAllObjectsAsync(bucket);
            foreach (var item in items)
            {
                string localPath = Path.Combine(destFolder, item.Key.Replace('/', Path.DirectorySeparatorChar));
                _transferManager.Enqueue(TransferDirection.Download, bucket, item.Key, localPath);
            }
            SetStatus($"Queued {items.Count} file(s) for download.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Delete Bucket ─────────────────────────────────────────────────────────
    private void CtxDeleteBucket_Click(object? sender, EventArgs e) => BeginInvoke(DoDeleteBucket);

    private async void DoDeleteBucket()
    {
        if (_s3 == null || lstBuckets.SelectedItem is not string bucket) return;

        var confirm = MessageBox.Show(
            $"Delete bucket '{bucket}' and ALL its contents?\n\nThis cannot be undone.",
            "Delete Bucket",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes) return;

        SetStatus($"Deleting bucket '{bucket}'…");
        try
        {
            await _s3.DeleteBucketAsync(bucket);
            if (_currentBucket == bucket)
            {
                _currentBucket = "";
                _currentPrefix = "";
                lvFiles.Items.Clear();
                UpdatePathLabel();
            }
            SetStatus($"Bucket '{bucket}' deleted.");
            await LoadBucketsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
            MessageBox.Show(ex.Message, "Delete Bucket Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Web URL ───────────────────────────────────────────────────────────────
    private void ToolStripWebUrl_Click(object? sender, EventArgs e) => BeginInvoke(DoWebUrl);

    private void DoWebUrl()
    {
        if (_s3 == null || lvFiles.SelectedItems.Count == 0) return;

        var items = lvFiles.SelectedItems.Cast<ListViewItem>()
            .Where(i => i.Tag is S3Item { Type: S3ItemType.File })
            .Select(i => (S3Item)i.Tag!)
            .ToList();

        if (items.Count == 0) return;

        using var dlg = new WebUrlForm(_s3, _currentBucket, items);
        dlg.ShowDialog(this);
    }

    // ── ACL ───────────────────────────────────────────────────────────────────
    private void CtxAcl_Click(object? sender, EventArgs e) => BeginInvoke(DoAcl);

    private void DoAcl()
    {
        if (_s3 == null) return;
        if (lvFiles.SelectedItems.Count != 1) return;
        if (lvFiles.SelectedItems[0].Tag is not S3Item { Type: S3ItemType.File } item) return;

        using var dlg = new AclForm(_s3, _currentBucket, item.Key);
        dlg.ShowDialog(this);
    }

    // ── Transfer panel events ─────────────────────────────────────────────────
    private void OnJobAdded(TransferJob job)
    {
        if (InvokeRequired) { BeginInvoke(() => OnJobAdded(job)); return; }

        var lvi = new ListViewItem(job.Direction == TransferDirection.Upload ? "↑" : "↓")
        {
            Tag = job,
            UseItemStyleForSubItems = false
        };
        lvi.SubItems.Add(job.FileName);
        lvi.SubItems.Add(job.Status.ToString());
        lvi.SubItems.Add("");   // progress (drawn)
        lvi.SubItems.Add("");   // size
        lvi.SubItems.Add("");   // speed

        _jobItems[job.Id] = lvi;
        lvTransfers.Items.Add(lvi);

        if (splitOuter.Panel2Collapsed)
            splitOuter.Panel2Collapsed = false;

        UpdateTransferButtons();
    }

    private readonly Dictionary<Guid, DateTime> _lastUiUpdate = new();
    private readonly Dictionary<Guid, (long bytes, DateTime time)> _speedSamples = new();

    private void OnJobChanged(TransferJob job)
    {
        // Throttle on the background thread only — skip if updated < 100 ms ago
        if (InvokeRequired)
        {
            var now = DateTime.UtcNow;
            lock (_lastUiUpdate)
            {
                if (_lastUiUpdate.TryGetValue(job.Id, out var last) &&
                    (now - last).TotalMilliseconds < 100 &&
                    job.Status == TransferStatus.Running)
                    return;
                _lastUiUpdate[job.Id] = now;
            }
            BeginInvoke(() => UpdateJobRow(job));
            return;
        }
        UpdateJobRow(job);
    }

    private void UpdateJobRow(TransferJob job)
    {
        if (!_jobItems.TryGetValue(job.Id, out var lvi)) return;

        // Speed — computed here on UI thread (single-threaded = no race condition)
        if (job.Status == TransferStatus.Running)
        {
            var now = DateTime.UtcNow;
            if (!_speedSamples.TryGetValue(job.Id, out var prev))
            {
                _speedSamples[job.Id] = (job.TransferredBytes, now);
            }
            else
            {
                double elapsed = (now - prev.time).TotalSeconds;
                if (elapsed >= 1.0)
                {
                    job.SpeedBytesPerSec = (job.TransferredBytes - prev.bytes) / elapsed;
                    _speedSamples[job.Id] = (job.TransferredBytes, now);
                }
            }
        }
        else
        {
            job.SpeedBytesPerSec = 0;
            _speedSamples.Remove(job.Id);
        }

        lvi.SubItems[TColStatus].Text = job.Status.ToString();
        lvi.SubItems[TColSize].Text   = job.TotalBytes > 0
            ? $"{FormatBytes(job.TransferredBytes)} / {FormatBytes(job.TotalBytes)}"
            : "";
        lvi.SubItems[TColSpeed].Text  = job.SpeedBytesPerSec > 0
            ? $"{FormatBytes((long)job.SpeedBytesPerSec)}/s"
            : "";

        bool dark = _settings.Theme == "Dark";
        lvi.ForeColor = job.Status switch
        {
            TransferStatus.Completed => dark ? Color.LimeGreen   : Color.DarkGreen,
            TransferStatus.Failed    => dark ? Color.Tomato      : Color.Firebrick,
            TransferStatus.Cancelled => SystemColors.GrayText,
            TransferStatus.Paused    => dark ? Color.Orange      : Color.DarkOrange,
            _                        => SystemColors.ControlText
        };

        lvTransfers.Invalidate(lvi.Bounds);
        UpdateTransferButtons();

        // Refresh file list when an upload completes
        if (job.Direction == TransferDirection.Upload && job.Status == TransferStatus.Completed)
            _ = LoadFilesAsync();
    }

    // ── Transfer actions ──────────────────────────────────────────────────────
    private TransferJob? SelectedJob =>
        lvTransfers.SelectedItems.Count > 0 ? lvTransfers.SelectedItems[0].Tag as TransferJob : null;

    private void PauseSelected()
    {
        if (SelectedJob is { } job) _transferManager?.Pause(job);
    }

    private void ResumeSelected()
    {
        if (SelectedJob is { } job) _transferManager?.Resume(job);
    }

    private void CancelSelected()
    {
        if (SelectedJob is { } job) _transferManager?.Cancel(job);
    }

    private void ClearCompleted()
    {
        _transferManager?.ClearCompleted();
        foreach (var id in _jobItems.Keys.ToList())
        {
            var lvi = _jobItems[id];
            var job = lvi.Tag as TransferJob;
            if (job?.Status is TransferStatus.Completed or TransferStatus.Cancelled)
            {
                lvTransfers.Items.Remove(lvi);
                _jobItems.Remove(id);
            }
        }
        UpdateTransferButtons();
    }

    private void UpdateTransferButtons()
    {
        var job = SelectedJob;
        btnPauseJob.Enabled  = job?.Status is TransferStatus.Running or TransferStatus.Pending;
        btnResumeJob.Enabled = job?.Status is TransferStatus.Paused or TransferStatus.Failed;
        btnCancelJob.Enabled = job?.Status is TransferStatus.Running or TransferStatus.Pending or TransferStatus.Paused;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void SetConnected(bool connected)
    {
        btnConnect.Enabled    = !connected;
        btnDisconnect.Enabled =  connected;
        btnNewBucket.Enabled  =  connected;
        btnUpload.Enabled     =  connected;
        btnDownload.Enabled   =  connected;
        btnDelete.Enabled     =  connected;
        btnNewFolder.Enabled  =  connected;
        btnCopyUrl.Enabled    =  connected;
        btnRefresh.Enabled    =  connected;
    }

    private void SetStatus(string msg) => statusLabel.Text = msg;

    // ── GitHub update check ───────────────────────────────────────────────────
    private static readonly HttpClient _http = new HttpClient();
    private async Task CheckForUpdateAsync()
    {
        try
        {
            _http.DefaultRequestHeaders.UserAgent.TryParseAdd("S3Lite-UpdateCheck/1.0");
            var json = await _http.GetStringAsync(
                "https://api.github.com/repos/sibercat/S3Lite/releases/latest");

            // Parse tag_name without a JSON library — it's always a short string
            var match = System.Text.RegularExpressions.Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""");
            if (!match.Success) return;

            string tag = match.Groups[1].Value.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var latest)) return;

            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null || latest <= current) return;

            // Newer version available — show clickable label in status bar
            _updateLabel.Text    = $"⬆ Update available: v{latest.Major}.{latest.Minor}.{latest.Build}";
            _updateLabel.Visible = true;
            _updateLabel.Click  += (_, _) =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "https://github.com/sibercat/S3Lite/releases") { UseShellExecute = true });
        }
        catch { /* silent — no internet, API rate limit, etc. */ }
    }

    private void UpdatePathLabel()
    {
        lblPath.Text = string.IsNullOrEmpty(_currentBucket)
            ? "  Not connected"
            : $"  s3://{_currentBucket}/{_currentPrefix}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0 / 1024.0:F1} GB";
        if (bytes >= 1024 * 1024)         return $"{bytes / 1024.0 / 1024.0:F1} MB";
        if (bytes >= 1024)                return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    private static string InputPrompt(string prompt, string title, string defaultValue = "")
    {
        var form  = new Form { Text = title, Size = new Size(440, 120), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false };
        var lbl   = new Label { Text = prompt, Left = 10, Top = 10, Width = 400 };
        var txt   = new TextBox { Left = 10, Top = 30, Width = 400, Text = defaultValue };
        var btnOk = new Button { Text = "OK",     Left = 250, Top = 55, Width = 75, DialogResult = DialogResult.OK };
        var btnCx = new Button { Text = "Cancel", Left = 335, Top = 55, Width = 75, DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { lbl, txt, btnOk, btnCx });
        form.AcceptButton = btnOk; form.CancelButton = btnCx;
        return form.ShowDialog() == DialogResult.OK ? txt.Text.Trim() : "";
    }

    // ── ListView subclass — erases empty space below items after native paint ─
    private class FileListView : ListView
    {
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_PAINT = 0x000F;
            if (m.Msg != WM_PAINT) return;
            try
            {
                int lastY = Items.Count > 0
                    ? Math.Max(0, GetItemRect(Items.Count - 1).Bottom)
                    : 0;
                int height = ClientSize.Height - lastY;
                if (height > 0)
                {
                    using var g = Graphics.FromHwnd(Handle);
                    g.FillRectangle(new SolidBrush(BackColor),
                        new Rectangle(0, lastY, ClientSize.Width, height));
                }
            }
            catch { /* visual only — safe to ignore */ }
        }
    }
}
