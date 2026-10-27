using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace Torray.App;

// A system-wide shortcut via RegisterHotKey, received by a message-only window.
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const uint NoRepeat = 0x4000;
    private static readonly nint MessageOnlyParent = -3;

    private readonly HwndSource _window;
    private readonly Action _pressed;
    private bool _registered;

    public GlobalHotkey(Action pressed)
    {
        _pressed = pressed;
        _window = new HwndSource(new HwndSourceParameters("TorrayHotkey") { ParentWindow = MessageOnlyParent, WindowStyle = 0 });
        _window.AddHook(OnMessage);
    }

    // Returns false when the text is invalid or another application already owns the shortcut.
    public bool Register(string? gesture)
    {
        Unregister();
        if (gesture is null)
        {
            return true;
        }

        if (!TryParse(gesture, out var modifiers, out var key))
        {
            return false;
        }

        // ModifierKeys uses the same bit values as the MOD_* flags.
        _registered = Native.RegisterHotKey(
            _window.Handle, HotkeyId, (uint)modifiers | NoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public void Dispose()
    {
        Unregister();
        _window.Dispose();
    }

    // A global shortcut needs Ctrl, Alt, or Win; Shift alone would swallow normal typing.
    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(text) is not KeyGesture gesture
                || (gesture.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
            {
                return false;
            }

            (modifiers, key) = (gesture.Modifiers, gesture.Key);
            return true;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0)
        {
            return null;
        }

        try
        {
            return new KeyGesture(key, modifiers).GetDisplayStringForCulture(CultureInfo.InvariantCulture);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void Unregister()
    {
        if (_registered)
        {
            Native.UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
    }

    private nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam == HotkeyId)
        {
            _pressed();
            handled = true;
        }

        return 0;
    }

    private static class Native
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(nint window, int id);
    }
}
