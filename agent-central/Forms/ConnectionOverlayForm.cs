using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Forms;

/// <summary>
/// Bağlantı aktifken ekranın sağ alt köşesinde (saatin üzerinde) gösterilen
/// sabit kare bilgi kutusu. Bağlantıyı yapan kullanıcının adını belirgin gösterir.
/// Sürüklenemez, odak çalmaz, her zaman üstte kalır.
/// </summary>
[SupportedOSPlatform("windows")]
public class ConnectionOverlayForm : Form
{
    private System.Windows.Forms.Timer _timer = null!;
    private readonly DateTime _connectedAt;
    private Label _durationLabel = null!;

    // Pencere gösterilirken odak çalmasını engelle (WS_EX_NOACTIVATE + TOPMOST + TOOLWINDOW)
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST    = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public ConnectionOverlayForm(string technicianName, string technicianIp)
    {
        _connectedAt    = DateTime.Now;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar   = false;
        TopMost         = true;
        StartPosition   = FormStartPosition.Manual; // konumu biz veriyoruz, Windows ezmesin
        BackColor       = Color.FromArgb(18, 20, 28);
        Opacity         = 0.94;
        Width           = 168;
        Height          = 150;

        BuildLayout(technicianName);
        PositionBottomRight();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Handle oluşup form gösterildikten sonra konumu kesinleştir (DPI/çalışma alanı netleşmiş olur)
        PositionBottomRight();
    }

    private void BuildLayout(string technicianName)
    {
        // İnce kırmızı üst şerit — "kayıt/izleme aktif" hissi
        var accent = new Panel
        {
            Dock = DockStyle.Top,
            Height = 4,
            BackColor = Color.FromArgb(220, 60, 60)
        };

        var statusLbl = new Label
        {
            Text = "● UZAK BAĞLANTI",
            ForeColor = Color.FromArgb(235, 90, 90),
            Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
            Bounds = new Rectangle(0, 12, Width, 16),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var captionLbl = new Label
        {
            Text = "Bağlanan kullanıcı",
            ForeColor = Color.FromArgb(140, 145, 165),
            Font = new Font("Segoe UI", 7.5f),
            Bounds = new Rectangle(0, 34, Width, 14),
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Kullanıcı adı — büyük ve belirgin (kutunun odağı)
        var nameLbl = new Label
        {
            Text = string.IsNullOrWhiteSpace(technicianName) ? "Teknisyen" : technicianName,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold),
            Bounds = new Rectangle(6, 50, Width - 12, 48),
            TextAlign = ContentAlignment.MiddleCenter,
            AutoEllipsis = true
        };

        _durationLabel = new Label
        {
            Text = "⏱ 00:00:00",
            ForeColor = Color.FromArgb(110, 205, 130),
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Bounds = new Rectangle(0, 104, Width, 22),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var hintLbl = new Label
        {
            Text = "ekranınız görüntüleniyor",
            ForeColor = Color.FromArgb(120, 125, 145),
            Font = new Font("Segoe UI", 7f),
            Bounds = new Rectangle(0, 128, Width, 14),
            TextAlign = ContentAlignment.MiddleCenter
        };

        Controls.AddRange(new Control[] { statusLbl, captionLbl, nameLbl, _durationLabel, hintLbl, accent });

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            var e = DateTime.Now - _connectedAt;
            _durationLabel.Text = $"⏱ {e:hh\\:mm\\:ss}";
        };
        _timer.Start();
    }

    private void PositionBottomRight()
    {
        // Çalışma alanı görev çubuğunu hariç tutar; sağ-alt köşeye, saatin hemen üzerine yapıştır.
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(area.Right - Width - 8, area.Bottom - Height - 8);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }
}
