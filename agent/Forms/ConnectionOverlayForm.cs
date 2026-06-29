using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.Agent.Forms;

/// <summary>
/// Sağ alt köşede sabit, her zaman üstte duran bağlantı bilgi formu.
/// Hangi teknisyenin bağlandığını, IP'sini ve ne kadar süredir bağlı olduğunu gösterir.
/// Kullanıcı sürükleyerek taşıyabilir.
/// </summary>
[SupportedOSPlatform("windows")]
public class ConnectionOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly DateTime _connectedAt;
    private Label _durationLabel = null!;
    private Label _techLabel = null!;
    private Label _ipLabel = null!;

    // Sürükleme
    private bool _dragging;
    private Point _dragStart;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public ConnectionOverlayForm(string technicianName, string technicianIp)
    {
        _connectedAt = DateTime.Now;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar    = false;
        TopMost          = true;
        BackColor        = Color.FromArgb(20, 20, 30);
        Opacity          = 0.93;
        Width            = 290;
        Height           = 90;

        BuildLayout(technicianName, technicianIp);
        PositionBottomRight();

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => UpdateDuration();
        _timer.Start();
    }

    private void BuildLayout(string technicianName, string technicianIp)
    {
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.FromArgb(15, 90, 200),
            Cursor = Cursors.SizeAll
        };

        var titleLabel = new Label
        {
            Text = "  Uzak Bağlantı Aktif",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.SizeAll
        };

        headerPanel.Controls.Add(titleLabel);
        headerPanel.MouseDown += StartDrag;
        headerPanel.MouseMove += DoDrag;
        headerPanel.MouseUp   += StopDrag;
        titleLabel.MouseDown  += StartDrag;
        titleLabel.MouseMove  += DoDrag;
        titleLabel.MouseUp    += StopDrag;

        _techLabel = new Label
        {
            Text = $"  👤 {technicianName}",
            ForeColor = Color.FromArgb(220, 220, 240),
            Font = new Font("Segoe UI", 8.5f),
            AutoSize = false,
            Bounds = new Rectangle(0, 26, 290, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _ipLabel = new Label
        {
            Text = $"  🌐 {technicianIp}",
            ForeColor = Color.FromArgb(160, 160, 190),
            Font = new Font("Segoe UI", 8f),
            AutoSize = false,
            Bounds = new Rectangle(0, 46, 170, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _durationLabel = new Label
        {
            Text = "  ⏱ 00:00:00",
            ForeColor = Color.FromArgb(100, 200, 120),
            Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
            AutoSize = false,
            Bounds = new Rectangle(170, 46, 120, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        Controls.Add(headerPanel);
        Controls.Add(_techLabel);
        Controls.Add(_ipLabel);
        Controls.Add(_durationLabel);
    }

    private void UpdateDuration()
    {
        var elapsed = DateTime.Now - _connectedAt;
        _durationLabel.Text = $"  ⏱ {elapsed:hh\\:mm\\:ss}";
    }

    private void PositionBottomRight()
    {
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - Width - 12, screen.Bottom - Height - 12);
    }

    // ── Drag to move ──────────────────────────────────────────────────────

    private void StartDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _dragging  = true;
        _dragStart = e.Location;
        if (sender is Control c) _dragStart = c.PointToScreen(e.Location);
        _dragStart = new Point(_dragStart.X - Left, _dragStart.Y - Top);
    }

    private void DoDrag(object? sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var screen = (sender as Control)?.PointToScreen(e.Location) ?? Cursor.Position;
        Location = new Point(screen.X - _dragStart.X, screen.Y - _dragStart.Y);
    }

    private void StopDrag(object? sender, MouseEventArgs e) => _dragging = false;

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }
}
