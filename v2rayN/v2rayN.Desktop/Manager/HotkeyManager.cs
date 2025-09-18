using System.Reactive.Linq;
using Avalonia.Input;
using Avalonia.ReactiveUI;
using Avalonia.Win32.Input;
#if HAS_GLOBALHOTKEYS
using GlobalHotKeys;
#endif

namespace v2rayN.Desktop.Manager;

public sealed class HotkeyManager
{
    private static readonly Lazy<HotkeyManager> _instance = new(() => new());
    public static HotkeyManager Instance = _instance.Value;

    private readonly Dictionary<int, EGlobalHotkey> _hotkeyTriggerDic = new();
    private Config? _config;
    private event Action<EGlobalHotkey>? _updateFunc;

    public bool IsPause { get; set; } = false;

    public void Init(Config config, Action<EGlobalHotkey> updateFunc)
    {
        _config = config;
        _updateFunc = updateFunc;
        Register();
    }

    public void Dispose()
    {
#if HAS_GLOBALHOTKEYS
        _hotKeyManager?.Dispose();
#endif
    }

    private void Register()
    {
#if HAS_GLOBALHOTKEYS
        if (_config!.GlobalHotkeys.Any(t => t.KeyCode > 0) == false)
        {
            return;
        }

        _hotKeyManager ??= new GlobalHotKeys.HotKeyManager();
        _hotkeyTriggerDic.Clear();

        foreach (var item in _config.GlobalHotkeys)
        {
            if (item.KeyCode is null or 0)
            {
                continue;
            }

            var vKey = KeyInterop.VirtualKeyFromKey((Key)item.KeyCode);
            var modifiers = Modifiers.None;
            if (item.Control)
            {
                modifiers |= Modifiers.Control;
            }
            if (item.Shift)
            {
                modifiers |= Modifiers.Shift;
            }
            if (item.Alt)
            {
                modifiers |= Modifiers.Alt;
            }

            var result = _hotKeyManager?.Register((VirtualKeyCode)vKey, modifiers);
            if (result?.IsSuccessful == true)
            {
                _hotkeyTriggerDic.Add(result.Id, item.EGlobalHotkey);
            }
        }

        _hotKeyManager?.HotKeyPressed
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(OnNext);
#else
        // No-op when GlobalHotKeys dependency is unavailable
        _hotkeyTriggerDic.Clear();
#endif
    }

#if HAS_GLOBALHOTKEYS
    private HotKeyManager? _hotKeyManager;

    private void OnNext(HotKey key)
    {
        if (_updateFunc == null || IsPause)
        {
            return;
        }

        if (_hotkeyTriggerDic.TryGetValue(key.Id, out var value))
        {
            _updateFunc?.Invoke(value);
        }
    }
#endif
}
