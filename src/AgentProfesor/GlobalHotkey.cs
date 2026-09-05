using System.Runtime.InteropServices;

namespace AgentProfesor;

/// <summary>System-wide hotkey (e.g. Ctrl+Alt+Space) that works even while another app has focus.</summary>
public sealed class GlobalHotkey : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 0xA1BE;

    private readonly HotkeyWindow _window;

    public event Action? Pressed;

    public GlobalHotkey(string hotkeySpec)
    {
        var (modifiers, key) = Parse(hotkeySpec);
        _window = new HotkeyWindow();
        _window.HotkeyPressed += () => Pressed?.Invoke();

        if (!RegisterHotKey(_window.Handle, HotkeyId, modifiers, (uint)key))
        {
            // Uklidit nativní okno, než vyhodíme výjimku – jinak by při obsazené zkratce zůstal
            // viset HWND (TrayContext výjimku jen zaloguje a objekt zahodí).
            _window.DestroyHandle();
            throw new InvalidOperationException($"Zkratku '{hotkeySpec}' se nepodařilo zaregistrovat (možná ji používá jiná appka).");
        }
    }

    private static (uint Modifiers, Keys Key) Parse(string spec)
    {
        uint modifiers = 0;
        var key = Keys.None;

        foreach (var part in spec.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= ModControl;
                    break;
                case "alt":
                    modifiers |= ModAlt;
                    break;
                case "shift":
                    modifiers |= ModShift;
                    break;
                case "win":
                case "windows":
                    modifiers |= ModWin;
                    break;
                case "space":
                    key = Keys.Space;
                    break;
                default:
                    if (Enum.TryParse<Keys>(part, ignoreCase: true, out var parsed))
                        key = parsed;
                    break;
            }
        }

        if (key == Keys.None)
            throw new ArgumentException($"Nerozpoznaná klávesa v zápisu zkratky '{spec}'.");

        return (modifiers, key);
    }

    public void Dispose()
    {
        UnregisterHotKey(_window.Handle, HotkeyId);
        _window.DestroyHandle();
    }

    private sealed class HotkeyWindow : NativeWindow
    {
        public event Action? HotkeyPressed;

        public HotkeyWindow() => CreateHandle(new CreateParams());

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
                HotkeyPressed?.Invoke();
            base.WndProc(ref m);
        }
    }
}
