using S3Lite.Services;

namespace S3Lite.Forms;

public class FilePreviewForm : Form
{
    private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".tif", ".ico" };

    private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml", ".csv", ".html", ".htm",
          ".css", ".js", ".ts", ".py", ".cs", ".java", ".cpp", ".c", ".h", ".sh", ".bat",
          ".ini", ".toml", ".cfg", ".conf", ".sql", ".env" };

    private readonly S3Service _s3;
    private readonly string    _bucket;
    private readonly string    _key;
    private readonly string    _fileName;
    private readonly int       _maxImageBytes;
    private readonly int       _maxTextBytes;

    private Label  lblStatus = null!;
    private Panel  pnlContent = null!;

    public FilePreviewForm(S3Service s3, string bucket, string key, int previewMaxMB = 10)
    {
        _s3           = s3;
        _bucket       = bucket;
        _key          = key;
        _fileName     = Path.GetFileName(key);
        _maxImageBytes = previewMaxMB * 1024 * 1024;
        _maxTextBytes  = Math.Min(previewMaxMB * 1024 * 1024, 512 * 1024); // text capped at 512KB
        InitializeComponent();
        Load += async (_, _) => await LoadPreviewAsync();
    }

    private void InitializeComponent()
    {
        Text            = $"Preview — {_fileName}";
        Size            = new Size(860, 620);
        MinimumSize     = new Size(500, 400);
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = true;
        MinimizeBox     = false;

        var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 36 };

        lblStatus = new Label
        {
            Left = 8, Top = 10, Width = 600, Height = 18,
            ForeColor = SystemColors.GrayText,
            Font = new Font(Font.FontFamily, 7.5f)
        };

        var btnClose = new Button
        {
            Text = "✖ Close", Width = 80, Height = 26,
            Left = 752, Top = 4
        };
        btnClose.Click += (_, _) => Close();

        pnlBottom.Controls.AddRange(new Control[] { lblStatus, btnClose });

        pnlContent = new Panel { Dock = DockStyle.Fill };

        Controls.Add(pnlContent);
        Controls.Add(pnlBottom);
        // CancelButton not used for non-modal forms
    }

    private async Task LoadPreviewAsync()
    {
        lblStatus.Text = "Loading preview…";
        var ext = Path.GetExtension(_fileName);

        if (ImageExts.Contains(ext))
            await LoadImagePreviewAsync();
        else if (TextExts.Contains(ext))
            await LoadTextPreviewAsync();
        else
            ShowUnsupported(ext);
    }

    private async Task LoadImagePreviewAsync()
    {
        try
        {
            var (data, total, _) = await _s3.GetObjectPreviewAsync(_bucket, _key, maxBytes: _maxImageBytes);

            if (data.Length < total)
            {
                ShowUnsupported(Path.GetExtension(_fileName), $"Image too large to preview ({FormatBytes(total)}). Use Download instead.");
                return;
            }

            using var ms = new MemoryStream(data);
            var img = Image.FromStream(ms);

            var pic = new PictureBox
            {
                Dock        = DockStyle.Fill,
                Image       = img,
                SizeMode    = PictureBoxSizeMode.Zoom,
                BackColor   = SystemColors.ControlDark
            };
            pnlContent.Controls.Add(pic);
            lblStatus.Text = $"{img.Width} × {img.Height} px  ·  {FormatBytes(total)}";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Failed to load image: {ex.Message}";
        }
    }

    private async Task LoadTextPreviewAsync()
    {
        try
        {
            var (data, total, _) = await _s3.GetObjectPreviewAsync(_bucket, _key, maxBytes: _maxTextBytes);
            string text = System.Text.Encoding.UTF8.GetString(data);

            var rtb = new RichTextBox
            {
                Dock       = DockStyle.Fill,
                ReadOnly   = true,
                Text       = text,
                Font       = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap   = false
            };
            pnlContent.Controls.Add(rtb);

            bool truncated = data.Length < total;
            lblStatus.Text = truncated
                ? $"Showing first {FormatBytes(data.Length)} of {FormatBytes(total)} — file truncated for preview"
                : $"{FormatBytes(total)}  ·  {text.Split('\n').Length:N0} lines";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Failed to load file: {ex.Message}";
        }
    }

    private void ShowUnsupported(string ext, string? message = null)
    {
        var lbl = new Label
        {
            Text      = message ?? $"Preview not available for {ext} files.",
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Font      = new Font(Font.FontFamily, 10f)
        };
        pnlContent.Controls.Add(lbl);
        lblStatus.Text = message ?? $"No preview for {ext}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
        if (bytes >= 1024 * 1024)         return $"{bytes / 1024.0 / 1024.0:F2} MB";
        if (bytes >= 1024)                return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
