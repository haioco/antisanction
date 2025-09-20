using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.ReactiveUI;
using Avalonia.Threading;
using DialogHostAvalonia;
using ReactiveUI;
using Splat;
using v2rayN.Desktop.Common;

namespace v2rayN.Desktop.Views;

public partial class StatusBarView : ReactiveUserControl<StatusBarViewModel>
{
    private static Config _config;

    public StatusBarView()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        ViewModel = Locator.Current.GetService<StatusBarViewModel>();
        ViewModel?.InitUpdateView(UpdateViewHandler);

        // Simplified interface - no UI element bindings needed since status bar is hidden
        this.WhenActivated(disposables =>
        {
            // Keep the ViewModel active but don't bind to any UI elements
        });
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.DispatcherRefreshIcon:
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshIcon();
                },
                DispatcherPriority.Default);
                break;

            case EViewAction.SetClipboardData:
                if (obj is null)
                    return false;
                await AvaUtils.SetClipboardData(this, (string)obj);
                break;

            case EViewAction.PasswordInput:
                return await PasswordInputAsync();
        }
        return await Task.FromResult(true);
    }

    private void RefreshIcon()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow.Icon = AvaUtils.GetAppIcon(_config.SystemProxyItem.SysProxyType);
            var iconslist = TrayIcon.GetIcons(Application.Current);
            iconslist[0].Icon = desktop.MainWindow.Icon;
            TrayIcon.SetIcons(Application.Current, iconslist);
        }
    }

    private async Task<bool> PasswordInputAsync()
    {
        var dialog = new SudoPasswordInputView();
        var obj = await DialogHost.Show(dialog);

        var password = obj?.ToString();
        if (password.IsNullOrEmpty())
        {
            return false;
        }

        AppManager.Instance.LinuxSudoPwd = password;
        return true;
    }

    // Simplified interface - no UI interactions needed
}
