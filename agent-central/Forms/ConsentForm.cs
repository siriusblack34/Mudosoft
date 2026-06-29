using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Orchestra.CentralAgent.Forms;

/// <summary>
/// Bir teknisyen ekranı görüntülemek istediğinde kullanıcıya gösterilen onay ekranı.
/// Tüm ekranı karartan tam ekran topmost bir katman + ortada onay kartı olarak gösterilir;
/// böylece odak-çalma kısıtlarından bağımsız olarak kullanıcının kaçırması imkânsızdır.
/// 60 saniye geri sayım sonunda otomatik reddeder.
/// DialogResult.Yes = onay verildi, diğerleri = reddedildi/kapatıldı.
/// </summary>
[SupportedOSPlatform("windows")]
public class ConsentForm : Form
{
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FLASHWINFO pfwi);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }
    private const uint FLASHW_ALL       = 3;
    private const uint FLASHW_TIMERNOFG = 12;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int  SW_RESTORE     = 9;

    private const int TIMEOUT_SECONDS = 60;
    private int _remaining = TIMEOUT_SECONDS;
    private readonly System.Windows.Forms.Timer _timer;
    private Button _acceptBtn = null!;
    private Button _denyBtn   = null!;
    private Label  _countdownLabel = null!;
    private ProgressBar _progressBar = null!;

    public ConsentForm(string requesterName, string requesterUsername)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        TopMost         = true;
        ShowInTaskbar   = true;
        BackColor       = Color.FromArgb(12, 14, 22);
        // NOT: Opacity KULLANMA — layered window (WS_EX_LAYERED) yapar; Dameware/mirror
        // sürücüleri layered pencereleri yakalayamadığı için uzaktan görünmez olur.
        // Solid tam ekran karartma her capture aracında ve fiziksel ekranda kesin görünür.
        DoubleBuffered  = true;

        // Tüm monitörleri kaplayacak şekilde (multi-monitor) tam ekran
        var vs = SystemInformation.VirtualScreen;
        Bounds = vs;

        BuildLayout(requesterName, requesterUsername, vs);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ForceForeground();

        var fi = new FLASHWINFO
        {
            cbSize    = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd      = Handle,
            dwFlags   = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount    = 0,
            dwTimeout = 0
        };
        FlashWindowEx(ref fi);
    }

    /// <summary>Pencereyi gerçekten öne/en üste taşır (AttachThreadInput ile odak kısıtını aşar).</summary>
    private void ForceForeground()
    {
        try
        {
            ShowWindow(Handle, SW_RESTORE);
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint thisThread = GetCurrentThreadId();

            if (fgThread != thisThread && fgThread != 0)
            {
                AttachThreadInput(fgThread, thisThread, true);
                BringWindowToTop(Handle);
                SetForegroundWindow(Handle);
                Activate();
                AttachThreadInput(fgThread, thisThread, false);
            }
            else
            {
                BringWindowToTop(Handle);
                SetForegroundWindow(Handle);
                Activate();
            }
            BringToFront();
            _acceptBtn?.Focus();
        }
        catch { }
    }

    private void BuildLayout(string requesterName, string requesterUsername, Rectangle vs)
    {
        const int cardW = 480, cardH = 300;

        // Kartı birincil ekranın ortasına yerleştir (form koordinatına çevir)
        var ps = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        int cardX = (ps.X - vs.X) + (ps.Width  - cardW) / 2;
        int cardY = (ps.Y - vs.Y) + (ps.Height - cardH) / 2;

        var card = new Panel
        {
            Bounds      = new Rectangle(cardX, cardY, cardW, cardH),
            BackColor   = Color.FromArgb(245, 247, 250),
            BorderStyle = BorderStyle.None
        };

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58,
            BackColor = Color.FromArgb(15, 90, 200)
        };
        header.Controls.Add(new Label
        {
            Text = "  Uzak Masaüstü Bağlantı İsteği",
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 12.5f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        });

        var msgLbl = new Label
        {
            Text = $"Aşağıdaki kişi ekranınızı görüntülemek istiyor:\n\n" +
                   $"👤  {requesterName}  ({requesterUsername})\n\n" +
                   "İzin vermek istiyor musunuz?",
            Font      = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(30, 30, 50),
            AutoSize  = false,
            Bounds    = new Rectangle(24, 74, 432, 96),
            TextAlign = ContentAlignment.TopLeft
        };

        _countdownLabel = new Label
        {
            Text      = $"Yanıt verilmezse {TIMEOUT_SECONDS} saniye içinde otomatik reddedilir",
            Font      = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gray,
            AutoSize  = false,
            Bounds    = new Rectangle(24, 176, 432, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _progressBar = new ProgressBar
        {
            Bounds  = new Rectangle(24, 200, 432, 8),
            Minimum = 0,
            Maximum = TIMEOUT_SECONDS,
            Value   = TIMEOUT_SECONDS,
            Style   = ProgressBarStyle.Continuous
        };

        _acceptBtn = new Button
        {
            Text      = "✓  İzin Ver",
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0, 140, 70),
            FlatStyle = FlatStyle.Flat,
            Bounds    = new Rectangle(24, 222, 200, 44),
            Cursor    = Cursors.Hand
        };
        _acceptBtn.FlatAppearance.BorderSize = 0;
        _acceptBtn.Click += (_, _) => { _timer.Stop(); DialogResult = DialogResult.Yes; Close(); };

        _denyBtn = new Button
        {
            Text      = "✕  Reddet",
            Font      = new Font("Segoe UI", 10f),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(200, 50, 50),
            FlatStyle = FlatStyle.Flat,
            Bounds    = new Rectangle(256, 222, 200, 44),
            Cursor    = Cursors.Hand
        };
        _denyBtn.FlatAppearance.BorderSize = 0;
        _denyBtn.Click += (_, _) => { _timer.Stop(); DialogResult = DialogResult.No; Close(); };

        card.Controls.AddRange(new Control[]
        {
            msgLbl, _countdownLabel, _progressBar, _acceptBtn, _denyBtn, header
        });

        Controls.Add(card);
        AcceptButton = _acceptBtn;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remaining--;
        _countdownLabel.Text = $"Yanıt verilmezse {_remaining} saniye içinde otomatik reddedilir";
        if (_remaining >= 0) _progressBar.Value = _remaining;

        // İlk birkaç saniye en üstte kalmayı garanti et (başka topmost pencereler araya girmesin)
        if (_remaining > TIMEOUT_SECONDS - 4)
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);

        if (_remaining <= 0)
        {
            _timer.Stop();
            DialogResult = DialogResult.No;
            Close();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer?.Dispose();
        base.Dispose(disposing);
    }
}
