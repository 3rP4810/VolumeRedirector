using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolumeRedirector.Services
{
    /// <summary>
    /// Tasti multimediali volume gestiti dall'hook.
    /// </summary>
    public enum VolumeKey
    {
        Up,
        Down,
        Mute
    }

    public class HotkeyService : IDisposable
    {
        // Costanti Win32
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int VK_VOLUME_UP = 0xAF;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_MUTE = 0xAD;

        private IntPtr _hookHandle = IntPtr.Zero;
        // IMPORTANTE: mantenere un riferimento al delegate per evitare che il GC lo raccolga
        private readonly LowLevelKeyboardProc _proc;

        // True mentre il tasto Muto, già gestito da noi, è tenuto premuto:
        // serve a ignorare le ripetizioni automatiche e a cambiare il muto una volta sola.
        private bool _muteHeld;

        // Chiamato a ogni pressione di Volume Su, Volume Giù o Volume Muto.
        // Restituisce true se il tasto è stato gestito (e va quindi bloccato),
        // false se va lasciato a Windows (volume o muto di sistema).
        public Func<VolumeKey, bool>? VolumeKeyHandler { get; set; }

        // Quando true, l'hook chiede a VolumeKeyHandler cosa fare con i tasti volume;
        // quando false, lascia passare i tasti normalmente.
        // Parte spento: l'app lo accende solo se esiste un'app collegata salvata.
        public bool IsActive { get; set; } = false;

        public HotkeyService()
        {
            _proc = HookCallback;
        }

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero) return; // già installato

            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                                           GetModuleHandle(curModule.ModuleName!), 0);

            if (_hookHandle == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Impossibile installare il keyboard hook. Errore: {Marshal.GetLastWin32Error()}");
        }

        public void Uninstall()
        {
            if (_hookHandle == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _muteHeld = false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                bool isDown = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                bool isUp = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

                if (isDown || isUp)
                {
                    var kbStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    // Rilascio del Muto già gestito da noi: fine della pressione, si blocca anche il rilascio
                    if (isUp && kbStruct.vkCode == VK_VOLUME_MUTE && _muteHeld)
                    {
                        _muteHeld = false;
                        return (IntPtr)1;
                    }

                    if (isDown && IsActive &&
                        (kbStruct.vkCode == VK_VOLUME_UP ||
                         kbStruct.vkCode == VK_VOLUME_DOWN ||
                         kbStruct.vkCode == VK_VOLUME_MUTE))
                    {
                        // Ripetizione automatica del Muto tenuto premuto: ignorata
                        if (kbStruct.vkCode == VK_VOLUME_MUTE && _muteHeld)
                            return (IntPtr)1;

                        VolumeKey key = kbStruct.vkCode == VK_VOLUME_UP ? VolumeKey.Up
                                      : kbStruct.vkCode == VK_VOLUME_DOWN ? VolumeKey.Down
                                      : VolumeKey.Mute;

                        bool handled;
                        try
                        {
                            handled = VolumeKeyHandler?.Invoke(key) ?? false;
                        }
                        catch
                        {
                            // In caso di errore il tasto non va perso: lo gestisce Windows
                            handled = false;
                        }

                        // Restituisce 1 per BLOCCARE il tasto e non modificare il volume di sistema
                        if (handled)
                        {
                            if (key == VolumeKey.Mute) _muteHeld = true;
                            return (IntPtr)1;
                        }
                    }
                }
            }

            // Lascia passare tutti gli altri tasti alla catena
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        public void Dispose() => Uninstall();

        // -------------------------
        // P/Invoke declarations
        // -------------------------
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                       IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode,
                                                     IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}
