using Orchestra.Shared.Enums;

namespace Orchestra.Shared.Dtos;

public class InputEventDto
{
    public InputEventType Type { get; set; }
    
    // Mouse
    public double X { get; set; } // Relative 0.0 - 1.0
    public double Y { get; set; } // Relative 0.0 - 1.0
    public int Button { get; set; } // 0: Left, 1: Middle, 2: Right
    
    // Keyboard
    public string Key { get; set; } = string.Empty; // JS Key Code or Char
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
}
