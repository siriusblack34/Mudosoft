namespace Orchestra.Tray;

public class StatusForm : Form
{
    private readonly AgentStatus _status;
    private readonly string _version;
    private readonly string _deviceId;
    private readonly DateTime _lastHeartbeat;

    public StatusForm(AgentStatus status, string version, string deviceId, DateTime lastHeartbeat)
    {
        _status = status;
        _version = version;
        _deviceId = deviceId;
        _lastHeartbeat = lastHeartbeat;

        InitializeForm();
        CreateContent();
    }

    private void InitializeForm()
    {
        Text = "Orchestra Durum";
        Size = new Size(320, 280);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(226, 232, 240);
        Font = new Font("Segoe UI", 9);
        ShowInTaskbar = false;

        // Position near system tray
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(screen.Right - Width - 20, screen.Bottom - Height - 20);
    }

    private void CreateContent()
    {
        // Header panel
        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(15)
        };

        var logoLabel = new Label
        {
            Text = "🛡️",
            Font = new Font("Segoe UI Emoji", 20),
            AutoSize = true,
            Location = new Point(15, 12)
        };
        headerPanel.Controls.Add(logoLabel);

        var titleLabel = new Label
        {
            Text = "Orchestra",
            Font = new Font("Segoe UI Semibold", 14),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(55, 10)
        };
        headerPanel.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = "Endpoint Security",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(148, 163, 184),
            AutoSize = true,
            Location = new Point(55, 32)
        };
        headerPanel.Controls.Add(subtitleLabel);

        Controls.Add(headerPanel);

        // Content panel
        var contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(15, 10, 15, 15)
        };

        var yPos = 70;

        // Status row
        AddStatusRow(contentPanel, "Durum", GetStatusText(), GetStatusColor(), ref yPos);
        
        // Version
        AddInfoRow(contentPanel, "Versiyon", _version, ref yPos);
        
        // Device ID
        AddInfoRow(contentPanel, "Cihaz ID", _deviceId.Length > 20 ? _deviceId[..20] + "..." : _deviceId, ref yPos);
        
        // Last heartbeat
        AddInfoRow(contentPanel, "Son Bağlantı", FormatTimeAgo(_lastHeartbeat), ref yPos);
        
        // Backend
        AddInfoRow(contentPanel, "Backend", "Bağlı", ref yPos);

        Controls.Add(contentPanel);

        // Close button
        var closeButton = new Button
        {
            Text = "Kapat",
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(51, 65, 85),
            ForeColor = Color.White,
            Size = new Size(80, 32),
            Location = new Point(Width - 100, Height - 60),
            Cursor = Cursors.Hand
        };
        closeButton.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
        closeButton.Click += (s, e) => Close();
        Controls.Add(closeButton);
    }

    private void AddStatusRow(Control parent, string label, string value, Color valueColor, ref int yPos)
    {
        var labelCtrl = new Label
        {
            Text = label,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(15, yPos)
        };
        parent.Controls.Add(labelCtrl);

        var statusPanel = new Panel
        {
            BackColor = Color.FromArgb(valueColor.R, valueColor.G, valueColor.B, 30),
            Size = new Size(100, 24),
            Location = new Point(Width - 130, yPos - 3)
        };

        var statusDot = new Label
        {
            Text = "●",
            ForeColor = valueColor,
            Font = new Font("Segoe UI", 10),
            AutoSize = true,
            Location = new Point(8, 2)
        };
        statusPanel.Controls.Add(statusDot);

        var valueCtrl = new Label
        {
            Text = value,
            ForeColor = valueColor,
            Font = new Font("Segoe UI Semibold", 9),
            AutoSize = true,
            Location = new Point(24, 4)
        };
        statusPanel.Controls.Add(valueCtrl);

        parent.Controls.Add(statusPanel);

        yPos += 30;
    }

    private void AddInfoRow(Control parent, string label, string value, ref int yPos)
    {
        var labelCtrl = new Label
        {
            Text = label,
            ForeColor = Color.FromArgb(148, 163, 184),
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(15, yPos)
        };
        parent.Controls.Add(labelCtrl);

        var valueCtrl = new Label
        {
            Text = value,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9),
            AutoSize = true,
            Location = new Point(Width - 180, yPos),
            TextAlign = ContentAlignment.MiddleRight
        };
        parent.Controls.Add(valueCtrl);

        yPos += 28;
    }

    private string GetStatusText() => _status switch
    {
        AgentStatus.Connected => "Korumalı",
        AgentStatus.Warning => "Uyarı",
        _ => "Bağlantı Yok"
    };

    private Color GetStatusColor() => _status switch
    {
        AgentStatus.Connected => Color.FromArgb(16, 185, 129),
        AgentStatus.Warning => Color.FromArgb(245, 158, 11),
        _ => Color.FromArgb(239, 68, 68)
    };

    private string FormatTimeAgo(DateTime utcTime)
    {
        if (utcTime == DateTime.MinValue) return "-";
        var diff = DateTime.UtcNow - utcTime;
        if (diff.TotalSeconds < 60) return $"{(int)diff.TotalSeconds}sn önce";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}dk önce";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}sa önce";
        return $"{(int)diff.TotalDays}g önce";
    }
}
