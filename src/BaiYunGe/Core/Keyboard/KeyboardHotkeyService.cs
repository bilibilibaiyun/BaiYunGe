using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace BaiYunGe.Core.Keyboard;

/// <summary>
/// WH_KEYBOARD_LL 全局低层键盘钩子。回调内只做按键过滤与识别，所有事件经
/// ThreadPool 异步派发，绝不在钩子回调里执行重活（避免被系统静默移除钩子）。
/// </summary>
public sealed class KeyboardHotkeyService : IDisposable
{
    private readonly object _sync = new();
    private readonly SynchronizationContext? _uiContext;
    private NativeMethods.LowLevelKeyboardProc? _hookProc;
    private IntPtr _hookHandle;
    private bool _disposed;

    private HotkeyDefinition _hotkey = HotkeyDefinition.Empty;
    private bool _recordingHotkey;
    private int _suppressKey;
    private bool _wakeKeyDown;

    public KeyboardHotkeyService()
    {
        // 捕获创建线程的同步上下文（App 在 UI 线程创建本服务）。
        // 事件改为按该上下文串行派发，保证 WakePressed 先于 WakeReleased 执行，
        // 消除 ThreadPool 并发导致的「快速按下松开时唤醒丢失 / 状态卡死」竞态。
        _uiContext = SynchronizationContext.Current;
    }

    public event EventHandler? WakePressed;

    public event EventHandler? WakeReleased;

    public event EventHandler? EscapePressed;

    public event Action<HotkeyDefinition>? HotkeyRecorded;

    public event Action? RecordCancelled;

    public void SetHotkey(HotkeyDefinition hotkey)
    {
        lock (_sync)
        {
            _hotkey = hotkey;
        }
    }

    public HotkeyDefinition CurrentHotkey
    {
        get
        {
            lock (_sync)
            {
                return _hotkey;
            }
        }
    }

    public void Start()
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            if (_hookHandle != IntPtr.Zero)
            {
                return;
            }

            _hookProc = HookCallback;
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookProc,
                Marshal.GetHINSTANCE(typeof(KeyboardHotkeyService).Module),
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed.");
            }
        }
    }

    /// <summary>进入快捷键录制：下一次有效按键组合将被捕获。</summary>
    public void BeginRecord()
    {
        lock (_sync)
        {
            _recordingHotkey = true;
        }
    }

    public void CancelRecord()
    {
        lock (_sync)
        {
            _recordingHotkey = false;
        }

        RecordCancelled?.Invoke();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var msg = (int)wParam;
        if (msg is not (NativeMethods.WM_KEYDOWN or NativeMethods.WM_KEYUP or
            NativeMethods.WM_SYSKEYDOWN or NativeMethods.WM_SYSKEYUP))
        {
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var info = Marshal.PtrToStructure<NativeMethods.KbdLowLevelHookStruct>(lParam);
        var isUp = (info.Flags & NativeMethods.LLKHF_UP) != 0 || msg is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP;
        var isInjected = (info.Flags & NativeMethods.LLKHF_INJECTED) != 0;

        if (isInjected)
        {
            // 过滤本程序/其他程序注入的模拟按键，避免误触发。
            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (!isUp && info.VkCode == NativeMethods.VK_ESCAPE)
        {
            if (CancelRecordingIfAny())
            {
                return 1;
            }

            RaiseAsync(() => EscapePressed?.Invoke(this, EventArgs.Empty));
        }

        if (_recordingHotkey && !isUp)
        {
            if (TryCaptureRecordedHotkey(info.VkCode))
            {
                return 1;
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (TryHandleWakeKey(info.VkCode, isUp))
        {
            return 1;
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool TryHandleWakeKey(int vkCode, bool isUp)
    {
        HotkeyDefinition hotkey;
        int suppressKey;
        lock (_sync)
        {
            hotkey = _hotkey;
            suppressKey = _suppressKey;
        }

        if (hotkey.IsEmpty)
        {
            return false;
        }

        // 主键抬起：无论修饰键是否已提前松开，都复位按下状态，避免状态卡死。
        if (isUp && vkCode == hotkey.Key)
        {
            var wasDown = ResetWakeKeyDown();
            if (wasDown)
            {
                RaiseAsync(() => WakeReleased?.Invoke(this, EventArgs.Empty));
            }

            return suppressKey == vkCode;
        }

        if (isUp)
        {
            return false;
        }

        // 只处理「主键」与「修饰键（Ctrl/Alt/Shift）」的按下；Win 键作主键时在此判定。
        var isPrimary = vkCode == hotkey.Key;
        var isModifier = NativeMethods.IsCtrlAltShift(vkCode);
        if (!isPrimary && !isModifier)
        {
            return false;
        }

        // 组合是否齐备：主键已按下 + 各修饰键状态匹配。无论主键还是修饰键先按下，
        // 只要组合凑齐就触发，从而支持「同时按下」快捷键（主键 keydown 先到时修饰键
        // 可能尚未按下，等修饰键 keydown 到达时再判定一次即可命中）。
        if (!IsComboDown(hotkey, vkCode))
        {
            return false;
        }

        if (suppressKey == vkCode)
        {
            // 该键当前需要被抑制（点按模式 / 按住录音期间）。
            return true;
        }

        if (!MarkWakeKeyDown())
        {
            // Windows 会以约 30ms 间隔重复派发 keydown；仅首次触发 WakePressed，重复的吞掉。
            return true;
        }

        RaiseAsync(() => WakePressed?.Invoke(this, EventArgs.Empty));
        return false;
    }

    /// <summary>
    /// 判断唤醒组合是否齐备：主键已按下，且各修饰键实时状态与快捷键定义一致。
    /// 用 GetAsyncKeyState 查询硬件实时状态，避免低级钩子回调里 GetKeyState 状态陈旧。
    /// </summary>
    private static bool IsComboDown(HotkeyDefinition hotkey, int vkCode)
    {
        // 主键是否已按下：当前事件即主键，或主键已物理按下。
        var primaryDown = vkCode == hotkey.Key || NativeMethods.IsKeyDown(hotkey.Key);
        if (!primaryDown)
        {
            return false;
        }

        var ctrlDown = NativeMethods.IsKeyDown(NativeMethods.VK_LCONTROL) || NativeMethods.IsKeyDown(NativeMethods.VK_RCONTROL);
        var altDown = NativeMethods.IsKeyDown(NativeMethods.VK_LMENU) || NativeMethods.IsKeyDown(NativeMethods.VK_RMENU);
        var shiftDown = NativeMethods.IsKeyDown(NativeMethods.VK_LSHIFT) || NativeMethods.IsKeyDown(NativeMethods.VK_RSHIFT);
        var winDown = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) || NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);

        // Win 作为修饰键：Win 键按下且主键不是 Win 键（主键是 Win 时它不算修饰键）。
        var winModifier = winDown && !NativeMethods.IsWinKey(hotkey.Key);

        return ctrlDown == hotkey.Ctrl &&
               altDown == hotkey.Alt &&
               shiftDown == hotkey.Shift &&
               winModifier == hotkey.Win;
    }

    /// <summary>标记唤醒键已按下；若已处于按下状态（自动重复）返回 false。</summary>
    private bool MarkWakeKeyDown()
    {
        lock (_sync)
        {
            if (_wakeKeyDown)
            {
                return false;
            }

            _wakeKeyDown = true;
            return true;
        }
    }

    /// <summary>重置唤醒键按下状态，返回重置前是否处于按下状态。</summary>
    private bool ResetWakeKeyDown()
    {
        lock (_sync)
        {
            var wasDown = _wakeKeyDown;
            _wakeKeyDown = false;
            return wasDown;
        }
    }

    /// <summary>录音期间抑制唤醒键本身，避免焦点跳动。</summary>
    public void SuppressWakeKey()
    {
        lock (_sync)
        {
            _suppressKey = _hotkey.Key;
        }
    }

    public void ReleaseWakeKey()
    {
        lock (_sync)
        {
            _suppressKey = 0;
        }
    }

    private bool CancelRecordingIfAny()
    {
        lock (_sync)
        {
            if (!_recordingHotkey)
            {
                return false;
            }

            _recordingHotkey = false;
        }

        RecordCancelled?.Invoke();
        return true;
    }

    private bool TryCaptureRecordedHotkey(int vkCode)
    {
        // 纯修饰键（Ctrl/Alt/Shift）不能作主键；Win 键允许作主键（如 Ctrl+Win）。
        if (NativeMethods.IsCtrlAltShift(vkCode))
        {
            return false;
        }

        var mods = NativeMethods.ModifierState();
        var isWinKey = NativeMethods.IsWinKey(vkCode);
        var captured = new HotkeyDefinition
        {
            Ctrl = mods.Ctrl,
            Alt = mods.Alt,
            Shift = mods.Shift,
            Win = mods.Win && !isWinKey, // Win 作为主键时，不再同时算作修饰键
            Key = vkCode
        };

        lock (_sync)
        {
            if (!_recordingHotkey)
            {
                return false;
            }

            _recordingHotkey = false;
            _hotkey = captured;
        }

        HotkeyRecorded?.Invoke(captured);
        return true;
    }

    private void RaiseAsync(Action action)
    {
        if (_uiContext is not null)
        {
            // UI 上下文串行派发：保证按键事件（按下/抬起）按发生顺序处理，避免竞态。
            _uiContext.Post(_ =>
            {
                try
                {
                    action();
                }
                catch
                {
                    // 事件处理器异常不能影响钩子。
                }
            }, null);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    action();
                }
                catch
                {
                    // 事件处理器异常不能影响钩子。
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _hookProc = null;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KeyboardHotkeyService));
        }
    }
}
