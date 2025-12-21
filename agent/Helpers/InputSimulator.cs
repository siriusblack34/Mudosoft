using System.Runtime.InteropServices;
using Mudosoft.Shared.Dtos;
using Mudosoft.Shared.Enums;

namespace Mudosoft.Agent.Helpers;

public class InputSimulator
{
    // ================= P/INVOKE =================
    [DllImport("user32.dll")]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

    [DllImport("user32.dll")]
    static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    // SM Constants for Virtual Screen
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    private const int MOUSEEVENTF_LEFTDOWN = 0x02;
    private const int MOUSEEVENTF_LEFTUP = 0x04;
    private const int MOUSEEVENTF_RIGHTDOWN = 0x08;
    private const int MOUSEEVENTF_RIGHTUP = 0x10;
    private const int MOUSEEVENTF_MIDDLEDOWN = 0x20;
    private const int MOUSEEVENTF_MIDDLEUP = 0x40;
    
    private const int KEYEVENTF_KEYUP = 0x0002;

    public void HandleInput(InputEventDto input)
    {
        // 1. Ekran Boyutlarını Al (Sanal Ekran Dahil)
        int vLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vTop = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (vWidth == 0) vWidth = GetSystemMetrics(0); // Fallback Primary
        if (vHeight == 0) vHeight = GetSystemMetrics(1);

        switch (input.Type)
        {
            case InputEventType.MouseMove:
                {
                    // Relative -> Absolute
                    int absX = vLeft + (int)(input.X * vWidth);
                    int absY = vTop + (int)(input.Y * vHeight);
                    SetCursorPos(absX, absY);
                }
                break;

            case InputEventType.MouseDown:
                {
                    int flags = 0;
                    if (input.Button == 0) flags = MOUSEEVENTF_LEFTDOWN;
                    else if (input.Button == 1) flags = MOUSEEVENTF_MIDDLEDOWN;
                    else if (input.Button == 2) flags = MOUSEEVENTF_RIGHTDOWN;
                    
                    mouse_event(flags, 0, 0, 0, 0);
                }
                break;

            case InputEventType.MouseUp:
                {
                    int flags = 0;
                    if (input.Button == 0) flags = MOUSEEVENTF_LEFTUP;
                    else if (input.Button == 1) flags = MOUSEEVENTF_MIDDLEUP;
                    else if (input.Button == 2) flags = MOUSEEVENTF_RIGHTUP;

                    mouse_event(flags, 0, 0, 0, 0);
                }
                break;

            case InputEventType.KeyDown:
                {
                    byte vk = ParseKey(input.Key);
                    if (vk > 0) keybd_event(vk, 0, 0, 0);
                }
                break;

            case InputEventType.KeyUp:
                {
                    byte vk = ParseKey(input.Key);
                    if (vk > 0) keybd_event(vk, 0, KEYEVENTF_KEYUP, 0);
                }
                break;
        }
    }

    private byte ParseKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return 0;

        // Özel Anahtarlar (Frontend'den gelen JS KeyCode'ları)
        if (key == "Backspace") return 0x08;
        if (key == "Tab") return 0x09;
        if (key == "Enter") return 0x0D;
        if (key == "Shift") return 0x10;
        if (key == "Control") return 0x11;
        if (key == "Alt") return 0x12;
        if (key == "CapsLock") return 0x14;
        if (key == "Escape") return 0x1B;
        if (key == "Space") return 0x20;
        if (key == "ArrowLeft") return 0x25;
        if (key == "ArrowUp") return 0x26;
        if (key == "ArrowRight") return 0x27;
        if (key == "ArrowDown") return 0x28;
        if (key == "Delete") return 0x2E;
        // ... Diğerleri eklenebilir

        // Tek Karakter (A, B, 1, 2 vs.)
        if (key.Length == 1)
        {
            try 
            {
                // Basit karakterler (Rakam, Harf)
                return (byte)VkKeyScan(key[0]);
            }
            catch { }
        }

        return 0;
    }
}
