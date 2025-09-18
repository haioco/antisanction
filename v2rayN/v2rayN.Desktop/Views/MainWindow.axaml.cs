using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DialogHostAvalonia;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Splat;
using v2rayN.Desktop.Base;
using v2rayN.Desktop.Common;
using v2rayN.Desktop.Manager;
using ServiceLib.Models;
using ServiceLib.Handler;

namespace v2rayN.Desktop.Views;

public partial class MainWindow : WindowBase<MainWindowViewModel>
{
    private static Config _config;
    private WindowNotificationManager? _manager;
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private bool _blCloseByUser = false;
    private readonly HttpClient _httpClient;
    private string? _haioAuthToken;
    private readonly string _tokenFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "haio-antisanction", "auth_token.txt");

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        // Initialize HTTP client with optional proxy
        var handler = new HttpClientHandler();
        try
        {
            // Try to use proxy if available, otherwise use direct connection
            var proxy = new System.Net.WebProxy("http://127.0.0.1:10810");
            handler.Proxy = proxy;
            handler.UseProxy = true;
        }
        catch
        {
            handler.UseProxy = false;
        }
        _httpClient = new HttpClient(handler);

        this.KeyDown += MainWindow_KeyDown;
        menuSettingsSetUWP.Click += menuSettingsSetUWP_Click;
        menuPromotion.Click += menuPromotion_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        menuClose.Click += MenuClose_Click;

        // HAIO Login Event Handlers
        btnRequestOtp.Click += BtnRequestOtp_Click;
        btnLogin.Click += BtnLogin_Click;
        btnLoginPassword.Click += BtnLoginPassword_Click;
        btnSkipLogin.Click += BtnSkipLogin_Click;

        ViewModel = new MainWindowViewModel(UpdateViewHandler);
        Locator.CurrentMutable.RegisterLazySingleton(() => ViewModel, typeof(MainWindowViewModel));

        // Show HAIO login dialog after window is loaded
        this.Loaded += MainWindow_Loaded;

        switch (_config.UiItem.MainGirdOrientation)
        {
            case EGirdOrientation.Horizontal:
                tabProfiles.Content ??= new ProfilesView(this);
                tabMsgView.Content ??= new MsgView();
                tabClashProxies.Content ??= new ClashProxiesView();
                tabClashConnections.Content ??= new ClashConnectionsView();
                gridMain.IsVisible = true;
                break;

            case EGirdOrientation.Vertical:
                tabProfiles1.Content ??= new ProfilesView(this);
                tabMsgView1.Content ??= new MsgView();
                tabClashProxies1.Content ??= new ClashProxiesView();
                tabClashConnections1.Content ??= new ClashConnectionsView();
                gridMain1.IsVisible = true;
                break;

            case EGirdOrientation.Tab:
            default:
                tabProfiles2.Content ??= new ProfilesView(this);
                tabMsgView2.Content ??= new MsgView();
                tabClashProxies2.Content ??= new ClashProxiesView();
                tabClashConnections2.Content ??= new ClashConnectionsView();
                gridMain2.IsVisible = true;
                break;
        }
        conTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);

            switch (_config.UiItem.MainGirdOrientation)
            {
                case EGirdOrientation.Horizontal:
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView.IsVisible).DisposeWith(disposables);
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies.IsVisible).DisposeWith(disposables);
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections.IsVisible).DisposeWith(disposables);
                    this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain.SelectedIndex).DisposeWith(disposables);
                    break;

                case EGirdOrientation.Vertical:
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabMsgView1.IsVisible).DisposeWith(disposables);
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies1.IsVisible).DisposeWith(disposables);
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections1.IsVisible).DisposeWith(disposables);
                    this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain1.SelectedIndex).DisposeWith(disposables);
                    break;

                case EGirdOrientation.Tab:
                default:
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashProxies2.IsVisible).DisposeWith(disposables);
                    this.OneWayBind(ViewModel, vm => vm.ShowClashUI, v => v.tabClashConnections2.IsVisible).DisposeWith(disposables);
                    this.Bind(ViewModel, vm => vm.TabMainSelectedIndex, v => v.tabMain2.SelectedIndex).DisposeWith(disposables);
                    break;
            }

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
              .AsObservable()
              .ObserveOn(RxApp.MainThreadScheduler)
              .Subscribe(content => Shutdown(content))
              .DisposeWith(disposables);
        });

        if (Utils.IsWindows())
        {
            this.Title = $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";

            ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
            HotkeyManager.Instance.Init(_config, OnHotkeyHandler);
        }
        else
        {
            this.Title = $"{Utils.GetVersion()}";

            menuRebootAsAdmin.IsVisible = false;
            menuSettingsSetUWP.IsVisible = false;
            menuGlobalHotkeySetting.IsVisible = false;
        }
        menuAddServerViaScan.IsVisible = false;

        AddHelpMenuItem();
    }

    #region Event

    private void OnProgramStarted(object state, bool timeout)
    {
        Dispatcher.UIThread.Post(() =>
                ShowHideWindow(true),
            DispatcherPriority.Default);
    }

    private async Task DelegateSnackMsg(string content)
    {
        _manager?.Show(new Notification(null, content, NotificationType.Information));
    }

    private async Task<bool> UpdateViewHandler(EViewAction action, object? obj)
    {
        switch (action)
        {
            case EViewAction.AddServerWindow:
                if (obj is null)
                    return false;
                return await new AddServerWindow((ProfileItem)obj).ShowDialog<bool>(this);

            case EViewAction.AddServer2Window:
                if (obj is null)
                    return false;
                return await new AddServer2Window((ProfileItem)obj).ShowDialog<bool>(this);

            case EViewAction.DNSSettingWindow:
                return await new DNSSettingWindow().ShowDialog<bool>(this);

            case EViewAction.FullConfigTemplateWindow:
                return await new FullConfigTemplateWindow().ShowDialog<bool>(this);

            case EViewAction.RoutingSettingWindow:
                return await new RoutingSettingWindow().ShowDialog<bool>(this);

            case EViewAction.OptionSettingWindow:
                return await new OptionSettingWindow().ShowDialog<bool>(this);

            case EViewAction.GlobalHotkeySettingWindow:
                return await new GlobalHotkeySettingWindow().ShowDialog<bool>(this);

            case EViewAction.SubSettingWindow:
                return await new SubSettingWindow().ShowDialog<bool>(this);

            case EViewAction.ShowHideWindow:
                Dispatcher.UIThread.Post(() =>
                    ShowHideWindow((bool?)obj),
                DispatcherPriority.Default);
                break;

            case EViewAction.ScanScreenTask:
                await ScanScreenTaskAsync();
                break;

            case EViewAction.ScanImageTask:
                await ScanImageTaskAsync();
                break;

            case EViewAction.AddServerViaClipboard:
                var clipboardData = await AvaUtils.GetClipboardData(this);
                if (clipboardData.IsNotEmpty() && ViewModel != null)
                {
                    await ViewModel.AddServerViaClipboardAsync(clipboardData);
                }
                break;
        }

        return await Task.FromResult(true);
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                Locator.Current.GetService<StatusBarViewModel>()?.SetListenerType((ESysProxyType)((int)e - 1));
                break;
        }
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_blCloseByUser)
        {
            return;
        }

        Logging.SaveLog("OnClosing -> " + e.CloseReason.ToString());

        switch (e.CloseReason)
        {
            case WindowCloseReason.OwnerWindowClosing or WindowCloseReason.WindowClosing:
                e.Cancel = true;
                ShowHideWindow(false);
                break;

            case WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown:
                await AppManager.Instance.AppExitAsync(false);
                break;
        }

        base.OnClosing(e);
    }

    private async void MainWindow_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)
        {
            switch (e.Key)
            {
                case Key.V:
                    var clipboardData = await AvaUtils.GetClipboardData(this);
                    if (clipboardData.IsNotEmpty() && ViewModel != null)
                    {
                        await ViewModel.AddServerViaClipboardAsync(clipboardData);
                    }
                    break;

                case Key.S:
                    await ScanScreenTaskAsync();
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void menuPromotion_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void menuSettingsSetUWP_Click(object? sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task ScanScreenTaskAsync()
    {
        //ShowHideWindow(false);

        NoticeManager.Instance.SendMessageAndEnqueue("Not yet implemented.(还未实现)");
        await Task.CompletedTask;
        //if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        //{
        //    //var bytes = QRCodeHelper.CaptureScreen(desktop);
        //    //await ViewModel?.ScanScreenResult(bytes);
        //}

        //ShowHideWindow(true);
    }

    private async Task ScanImageTaskAsync()
    {
        var fileName = await UI.OpenFileDialog(this, null);
        if (fileName.IsNullOrEmpty())
        {
            return;
        }

        if (ViewModel != null)
        {
            await ViewModel.ScanImageResult(fileName);
        }
    }

    private void MenuCheckUpdate_Click(object? sender, RoutedEventArgs e)
    {
        _checkUpdateView ??= new CheckUpdateView();
        DialogHost.Show(_checkUpdateView);
    }

    private void MenuBackupAndRestore_Click(object? sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView(this);
        DialogHost.Show(_backupAndRestoreView);
    }

    private async void MenuClose_Click(object? sender, RoutedEventArgs e)
    {
        if (await UI.ShowYesNo(this, ResUI.menuExitTips) != ButtonResult.Yes)
        {
            return;
        }

        _blCloseByUser = true;
        StorageUI();

        await AppManager.Instance.AppExitAsync(true);
    }

    private void Shutdown(bool obj)
    {
        if (obj is bool b && _blCloseByUser == false)
        {
            _blCloseByUser = b;
        }
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            HotkeyManager.Instance.Dispose();
            desktop.Shutdown();
        }
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? (!_config.UiItem.ShowInTaskbar ^ (WindowState == WindowState.Minimized));
        if (bl)
        {
            this.Show();
            if (this.WindowState == WindowState.Minimized)
            {
                this.WindowState = WindowState.Normal;
            }
            this.Activate();
            this.Focus();
        }
        else
        {
            if (Utils.IsLinux() && _config.UiItem.Hide2TrayWhenClose == false)
            {
                this.WindowState = WindowState.Minimized;
                return;
            }

            foreach (var ownedWindow in this.OwnedWindows)
            {
                ownedWindow.Close();
            }
            this.Hide();
        }

        _config.UiItem.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        RestoreUI();
    }

    private void RestoreUI()
    {
        if (_config.UiItem.MainGirdHeight1 > 0 && _config.UiItem.MainGirdHeight2 > 0)
        {
            if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
            {
                gridMain.ColumnDefinitions[0].Width = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain.ColumnDefinitions[2].Width = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
            else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
            {
                gridMain1.RowDefinitions[0].Height = new GridLength(_config.UiItem.MainGirdHeight1, GridUnitType.Star);
                gridMain1.RowDefinitions[2].Height = new GridLength(_config.UiItem.MainGirdHeight2, GridUnitType.Star);
            }
        }
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);

        if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Horizontal)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain.ColumnDefinitions[0].ActualWidth, gridMain.ColumnDefinitions[2].ActualWidth);
        }
        else if (_config.UiItem.MainGirdOrientation == EGirdOrientation.Vertical)
        {
            ConfigHandler.SaveMainGirdHeight(_config, gridMain1.RowDefinitions[0].ActualHeight, gridMain1.RowDefinitions[2].ActualHeight);
        }
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType != ECoreType.v2fly
                        && t.CoreType != ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url?.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag?.ToString());
        }
    }

    #endregion UI

    #region HAIO Authentication

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Check for existing auth token first
        await Task.Delay(500); // Small delay to ensure UI is ready
        await CheckExistingAuthToken();
    }

    private async Task CheckExistingAuthToken()
    {
        try
        {
            var savedToken = await LoadAuthToken();
            
            if (!string.IsNullOrEmpty(savedToken))
            {
                Console.WriteLine($"[DEBUG] Found saved auth token, validating...");
                
                if (await ValidateAuthToken(savedToken))
                {
                    Console.WriteLine($"[DEBUG] Saved token is valid, auto-logging in...");
                    _haioAuthToken = savedToken;
                    await OnLoginSuccess();
                    return; // Don't show login dialog
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Saved token is expired, deleting and showing login dialog");
                    DeleteAuthToken();
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] No saved token found");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error checking existing token: {ex.Message}");
        }
        
        // If we reach here, show the login dialog
        ShowHaioLoginDialog();
    }

    private void ShowHaioLoginDialog()
    {
        DialogHost.Show(haioLoginDialog);
    }

    private async void BtnRequestOtp_Click(object? sender, RoutedEventArgs e)
    {
        var mobile = txtOtpMobile.Text?.Trim();
        if (string.IsNullOrEmpty(mobile) || !IsValidMobileNumber(mobile))
        {
            ShowLoginStatus("Please enter a valid mobile number (e.g. +989123456789)", isError: true);
            return;
        }

        btnRequestOtp.IsEnabled = false;
        btnRequestOtp.Content = "Sending...";
        
        try
        {
            await RequestOtp(mobile);
            otpStatusBorder.IsVisible = true;
            txtOtpCode.IsEnabled = true;
            btnLogin.IsEnabled = true;
            ShowLoginStatus("Verification code sent successfully to your mobile", isError: false);
        }
        catch (Exception ex)
        {
            ShowLoginStatus($"Failed to send verification code: {ex.Message}", isError: true);
            btnRequestOtp.IsEnabled = true;
            btnRequestOtp.Content = "Send Verification Code";
        }
    }

    private async void BtnLogin_Click(object? sender, RoutedEventArgs e)
    {
        var mobile = txtOtpMobile.Text?.Trim();
        var otpCode = txtOtpCode.Text?.Trim();

        if (string.IsNullOrEmpty(mobile) || string.IsNullOrEmpty(otpCode))
        {
            ShowLoginStatus("Please enter mobile number and verification code", isError: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(otpCode) || otpCode.Length < 4 || otpCode.Length > 8 || !otpCode.All(c => char.IsLetterOrDigit(c)))
        {
            ShowLoginStatus("Verification code must be 4-8 alphanumeric characters", isError: true);
            return;
        }

        btnLogin.IsEnabled = false;
        btnLogin.Content = "Verifying...";

        try
        {
            await LoginWithOtp(mobile, otpCode);
        }
        catch (Exception ex)
        {
            ShowLoginStatus($"Verification failed: {ex.Message}", isError: true);
            btnLogin.IsEnabled = true;
            btnLogin.Content = "Verify & Login";
        }
    }

    private async void BtnLoginPassword_Click(object? sender, RoutedEventArgs e)
    {
        var email = txtPasswordEmail.Text?.Trim();
        var password = txtPassword.Text?.Trim();

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            ShowLoginStatus("Please enter both email and password", isError: true);
            return;
        }

        if (!IsValidEmailAddress(email))
        {
            ShowLoginStatus("Please enter a valid email address", isError: true);
            return;
        }

        btnLoginPassword.IsEnabled = false;
        btnLoginPassword.Content = "Logging in...";

        try
        {
            await LoginWithEmailPassword(email, password);
        }
        catch (Exception ex)
        {
            ShowLoginStatus($"Login failed: {ex.Message}", isError: true);
            btnLoginPassword.IsEnabled = true;
            btnLoginPassword.Content = "Login with Email/Password";
        }
    }

    private void BtnSkipLogin_Click(object? sender, RoutedEventArgs e)
    {
        HideHaioLoginDialog();
        ShowLoginStatus("Continuing with limited features", isError: false);
    }

    private async Task RequestOtp(string mobile)
    {
        var payload = new { mobile = mobile };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine($"[DEBUG] Sending OTP request to: https://api.haio.ir/v1/user/otp/login/");
        Console.WriteLine($"[DEBUG] OTP Request payload: {json}");

        var response = await _httpClient.PostAsync("https://api.haio.ir/v1/user/otp/login/", content);
        var responseText = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[DEBUG] OTP Request Response status: {response.StatusCode}");
        Console.WriteLine($"[DEBUG] OTP Request Response body: {responseText}");

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = "Failed to send OTP";
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(responseText);
                Console.WriteLine($"[DEBUG] OTP Request Parsed error JSON: {errorJson}");
                if (errorJson.TryGetProperty("message", out var message))
                {
                    errorMsg = message.GetString() ?? errorMsg;
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[DEBUG] OTP Request Error parsing error response: {ex.Message}");
            }
            throw new Exception(errorMsg);
        }

        Console.WriteLine($"[DEBUG] OTP requested successfully for mobile: {mobile}");
        Logging.SaveLog($"OTP requested successfully for mobile: {mobile}");
    }

    private async Task LoginWithOtp(string mobile, string otpCode)
    {
        var payload = new { mobile = mobile, otp_code = otpCode };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine($"[DEBUG] Sending OTP verify request to: https://api.haio.ir/v1/user/otp/login/verify/");
        Console.WriteLine($"[DEBUG] Request payload: {json}");

        var response = await _httpClient.PostAsync("https://api.haio.ir/v1/user/otp/login/verify/", content);
        var responseText = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[DEBUG] OTP Response status: {response.StatusCode}");
        Console.WriteLine($"[DEBUG] OTP Response body: {responseText}");

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = "Invalid verification code";
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(responseText);
                Console.WriteLine($"[DEBUG] OTP Parsed error JSON: {errorJson}");
                if (errorJson.TryGetProperty("message", out var message))
                {
                    errorMsg = message.GetString() ?? errorMsg;
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[DEBUG] OTP Error parsing error response: {ex.Message}");
            }
            throw new Exception(errorMsg);
        }

        try
        {
            var loginResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
            Console.WriteLine($"[DEBUG] OTP Parsed login response JSON: {loginResponse}");
            
            // Print all properties in the response
            if (loginResponse.ValueKind == JsonValueKind.Object)
            {
                Console.WriteLine("[DEBUG] OTP Response properties:");
                foreach (var property in loginResponse.EnumerateObject())
                {
                    Console.WriteLine($"[DEBUG]   - {property.Name}: {property.Value}");
                }
            }

            // Try to extract token from nested structure: params.data.access_token
            string? tokenValue = null;
            
            // First check if we have params property
            if (loginResponse.TryGetProperty("params", out var paramsElement))
            {
                Console.WriteLine($"[DEBUG] OTP Found 'params' property");
                
                // Then check if we have data property inside params
                if (paramsElement.TryGetProperty("data", out var dataElement))
                {
                    Console.WriteLine($"[DEBUG] OTP Found 'data' property inside params");
                    
                    // Now try to get access_token from data
                    if (dataElement.TryGetProperty("access_token", out var tokenElement))
                    {
                        tokenValue = tokenElement.GetString();
                        Console.WriteLine($"[DEBUG] OTP Found access_token in params.data: {(tokenValue != null ? tokenValue.Substring(0, Math.Min(20, tokenValue.Length)) + "..." : "null")}");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] OTP access_token not found in params.data");
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] OTP 'data' property not found inside params");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] OTP 'params' property not found in response");
                
                // Fallback: try direct token fields at root level
                string[] possibleTokenFields = { "access_token", "token", "access", "accessToken", "auth_token", "authToken" };
                
                foreach (var fieldName in possibleTokenFields)
                {
                    if (loginResponse.TryGetProperty(fieldName, out var tokenField))
                    {
                        tokenValue = tokenField.GetString();
                        Console.WriteLine($"[DEBUG] OTP Found token in root field '{fieldName}': {(tokenValue != null ? tokenValue.Substring(0, Math.Min(10, tokenValue.Length)) + "..." : "null")}");
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(tokenValue))
            {
                _haioAuthToken = tokenValue;
                SaveAuthToken(tokenValue); // Save token to disk
                await OnLoginSuccess();
            }
            else
            {
                Console.WriteLine("[DEBUG] OTP No token found in any expected field");
                throw new Exception("Login response missing access token");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] OTP Exception during response parsing: {ex.Message}");
            throw;
        }
    }

    private async Task LoginWithEmailPassword(string email, string password)
    {
        var payload = new { email = email, password = password };
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        Console.WriteLine($"[DEBUG] Sending login request to: https://api.haio.ir/v1/user/login/");
        Console.WriteLine($"[DEBUG] Request payload: {json}");

        var response = await _httpClient.PostAsync("https://api.haio.ir/v1/user/login/", content);
        var responseText = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[DEBUG] Response status: {response.StatusCode}");
        Console.WriteLine($"[DEBUG] Response headers: {response.Headers}");
        Console.WriteLine($"[DEBUG] Response body: {responseText}");

        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = "Invalid email or password";
            try
            {
                var errorJson = JsonSerializer.Deserialize<JsonElement>(responseText);
                Console.WriteLine($"[DEBUG] Parsed error JSON: {errorJson}");
                if (errorJson.TryGetProperty("message", out var message))
                {
                    errorMsg = message.GetString() ?? errorMsg;
                }
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"[DEBUG] Error parsing error response: {ex.Message}");
            }
            throw new Exception(errorMsg);
        }

        try
        {
            var loginResponse = JsonSerializer.Deserialize<JsonElement>(responseText);
            Console.WriteLine($"[DEBUG] Parsed login response JSON: {loginResponse}");
            
            // Print all properties in the response
            if (loginResponse.ValueKind == JsonValueKind.Object)
            {
                Console.WriteLine("[DEBUG] Response properties:");
                foreach (var property in loginResponse.EnumerateObject())
                {
                    Console.WriteLine($"[DEBUG]   - {property.Name}: {property.Value}");
                }
            }

            // Try to extract token from nested structure: params.data.access_token
            string? tokenValue = null;
            
            // First check if we have params property
            if (loginResponse.TryGetProperty("params", out var paramsElement))
            {
                Console.WriteLine($"[DEBUG] Found 'params' property");
                
                // Then check if we have data property inside params
                if (paramsElement.TryGetProperty("data", out var dataElement))
                {
                    Console.WriteLine($"[DEBUG] Found 'data' property inside params");
                    
                    // Now try to get access_token from data
                    if (dataElement.TryGetProperty("access_token", out var tokenElement))
                    {
                        tokenValue = tokenElement.GetString();
                        Console.WriteLine($"[DEBUG] Found access_token in params.data: {(tokenValue != null ? tokenValue.Substring(0, Math.Min(20, tokenValue.Length)) + "..." : "null")}");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] access_token not found in params.data");
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] 'data' property not found inside params");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] 'params' property not found in response");
                
                // Fallback: try direct token fields at root level
                string[] possibleTokenFields = { "access_token", "token", "access", "accessToken", "auth_token", "authToken" };
                
                foreach (var fieldName in possibleTokenFields)
                {
                    if (loginResponse.TryGetProperty(fieldName, out var tokenField))
                    {
                        tokenValue = tokenField.GetString();
                        Console.WriteLine($"[DEBUG] Found token in root field '{fieldName}': {(tokenValue != null ? tokenValue.Substring(0, Math.Min(10, tokenValue.Length)) + "..." : "null")}");
                        break;
                    }
                }
            }

            if (!string.IsNullOrEmpty(tokenValue))
            {
                _haioAuthToken = tokenValue;
                SaveAuthToken(tokenValue); // Save token to disk
                await OnLoginSuccess();
            }
            else
            {
                Console.WriteLine("[DEBUG] No token found in any expected field");
                throw new Exception("Login response missing access token");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception during response parsing: {ex.Message}");
            throw;
        }
    }

    private async Task OnLoginSuccess()
    {
        Logging.SaveLog($"HAIO authentication successful, token received");
        
        // Fetch anti-sanction packages
        try
        {
            await FetchAntiSanctionPackages();
            
            // Setup domain-based routing rules
            await SetupHaioRoutingRules();
            
            HideHaioLoginDialog();
            ShowLoginStatus("Login successful! HAIO servers and routing configured.", isError: false);
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Failed to fetch anti-sanction packages: {ex.Message}");
            HideHaioLoginDialog();
            ShowLoginStatus("Login successful, but failed to load packages.", isError: false);
        }
    }

    private async Task FetchAntiSanctionPackages()
    {
        if (string.IsNullOrEmpty(_haioAuthToken))
            return;

        Console.WriteLine($"[DEBUG] Fetching anti-sanction packages with token: {(_haioAuthToken.Length > 20 ? _haioAuthToken.Substring(0, 20) + "..." : _haioAuthToken)}");

        _httpClient.DefaultRequestHeaders.Authorization = 
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _haioAuthToken);

        Console.WriteLine($"[DEBUG] Sending request to: https://api.haio.ir/v1/anti/sanction/?page=0&filter=");
        var response = await _httpClient.GetAsync("https://api.haio.ir/v1/anti/sanction/?page=0&filter=");
        var responseText = await response.Content.ReadAsStringAsync();

        Console.WriteLine($"[DEBUG] Anti-sanction response status: {response.StatusCode}");
        Console.WriteLine($"[DEBUG] Anti-sanction response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
        Console.WriteLine($"[DEBUG] Anti-sanction response body: {responseText}");

        if (response.IsSuccessStatusCode)
        {
            Logging.SaveLog($"Anti-sanction packages fetched: {responseText.Length} bytes");
            
            try
            {
                await ProcessAntiSanctionPackages(responseText);
                Console.WriteLine($"[DEBUG] Successfully processed anti-sanction packages");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Error processing anti-sanction packages: {ex.Message}");
                throw new Exception($"Failed to process packages: {ex.Message}");
            }
        }
        else
        {
            throw new Exception($"Failed to fetch packages: {response.StatusCode} - {responseText}");
        }
    }

    private async Task ProcessAntiSanctionPackages(string responseJson)
    {
        try
        {
            var packagesResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);
            Console.WriteLine($"[DEBUG] Parsed anti-sanction response JSON: {packagesResponse}");
            
            // Print all properties in the response
            if (packagesResponse.ValueKind == JsonValueKind.Object)
            {
                Console.WriteLine("[DEBUG] Anti-sanction response properties:");
                foreach (var property in packagesResponse.EnumerateObject())
                {
                    Console.WriteLine($"[DEBUG]   - {property.Name}: {property.Value}");
                }
            }

            // Look for packages in different possible locations
            JsonElement packagesArray = default;
            bool foundPackages = false;
            
            // Try params.data structure (this is the correct structure based on API response)
            if (packagesResponse.TryGetProperty("params", out var paramsElement))
            {
                Console.WriteLine($"[DEBUG] Found 'params' property in anti-sanction response");
                if (paramsElement.TryGetProperty("data", out var dataElement))
                {
                    Console.WriteLine($"[DEBUG] Found 'data' property inside params");
                    if (dataElement.ValueKind == JsonValueKind.Array)
                    {
                        packagesArray = dataElement;
                        foundPackages = true;
                        Console.WriteLine($"[DEBUG] Found packages array in params.data with {packagesArray.GetArrayLength()} items");
                    }
                }
            }
            
            // Try direct data property
            if (!foundPackages && packagesResponse.TryGetProperty("data", out var directDataElement))
            {
                Console.WriteLine($"[DEBUG] Found 'data' property at root level");
                if (directDataElement.ValueKind == JsonValueKind.Array)
                {
                    packagesArray = directDataElement;
                    foundPackages = true;
                    Console.WriteLine($"[DEBUG] Found packages array in root data with {packagesArray.GetArrayLength()} items");
                }
                else if (directDataElement.TryGetProperty("packages", out var directPackages) && directPackages.ValueKind == JsonValueKind.Array)
                {
                    packagesArray = directPackages;
                    foundPackages = true;
                    Console.WriteLine($"[DEBUG] Found packages array in root data.packages with {packagesArray.GetArrayLength()} items");
                }
            }
            
            // Try direct packages property at root
            if (!foundPackages && packagesResponse.TryGetProperty("packages", out var rootPackages) && rootPackages.ValueKind == JsonValueKind.Array)
            {
                packagesArray = rootPackages;
                foundPackages = true;
                Console.WriteLine($"[DEBUG] Found packages array at root level with {packagesArray.GetArrayLength()} items");
            }

            if (!foundPackages)
            {
                Console.WriteLine($"[DEBUG] No packages array found in response structure");
                return;
            }

            // Process each package
            foreach (var package in packagesArray.EnumerateArray())
            {
                Console.WriteLine($"[DEBUG] Processing package: {package}");
                await ProcessSinglePackage(package);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Exception processing anti-sanction packages: {ex.Message}");
            throw;
        }
    }

    private async Task ProcessSinglePackage(JsonElement package)
    {
        try
        {
            // Print package properties
            Console.WriteLine("[DEBUG] Package properties:");
            foreach (var property in package.EnumerateObject())
            {
                Console.WriteLine($"[DEBUG]   - {property.Name}: {property.Value}");
            }

            // Each package contains a ready-to-use config field with the complete Trojan URL
            if (package.TryGetProperty("config", out var configElement))
            {
                string? configUrl = configElement.GetString();
                if (!string.IsNullOrEmpty(configUrl))
                {
                    Console.WriteLine($"[DEBUG] Found config URL: {configUrl}");
                    
                    // Get additional info for logging
                    string? title = package.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : "Unknown";
                    string? domain = package.TryGetProperty("domain", out var domainElement) ? domainElement.GetString() : "Unknown";
                    bool isOver = package.TryGetProperty("is_over", out var isOverElement) && isOverElement.GetInt32() == 1;
                    
                    Console.WriteLine($"[DEBUG] Server: {title}, Domain: {domain}, Is Expired: {isOver}");
                    
                    // Only add servers that are not expired (is_over == 0)
                    if (!isOver)
                    {
                        await AddTrojanConfigurationFromUrl(configUrl, title ?? "HaioGateway");
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Skipping expired server: {title}");
                        Logging.SaveLog($"Skipped expired HAIO server: {title} ({domain})");
                    }
                }
                else
                {
                    Console.WriteLine($"[DEBUG] Config field is empty");
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] No config field found in package");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error processing single package: {ex.Message}");
        }
    }

    private async Task AddTrojanConfigurationFromUrl(string configUrl, string serverName)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Adding Trojan configuration: {configUrl}");
            
            // Check if this server already exists
            if (await IsServerAlreadyExists(configUrl, serverName))
            {
                Console.WriteLine($"[DEBUG] Server already exists, skipping: {serverName}");
                Logging.SaveLog($"HAIO server already exists, skipping: {serverName}");
                return;
            }
            
            // Add to v2rayN using existing clipboard functionality
            Logging.SaveLog($"Adding new HAIO Trojan server: {serverName}");
            
            // Use the existing method to add server via clipboard data
            await ViewModel.AddServerViaClipboardAsync(configUrl);
            
            Console.WriteLine($"[DEBUG] Successfully added Trojan server: {serverName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error adding Trojan configuration: {ex.Message}");
            Logging.SaveLog($"Failed to add Trojan server {serverName}: {ex.Message}");
        }
    }

    private async Task<bool> IsServerAlreadyExists(string configUrl, string serverName)
    {
        try
        {
            // Parse the Trojan URL to get server details
            var uri = new Uri(configUrl);
            var serverAddress = uri.Host;
            var serverPort = uri.Port;
            
            Console.WriteLine($"[DEBUG] Checking for existing server: {serverAddress}:{serverPort}");
            
            // Get all existing profile items
            var existingProfiles = await AppManager.Instance.ProfileItems("");
            
            if (existingProfiles != null)
            {
                // Check if any existing server matches this address and port
                foreach (var profile in existingProfiles)
                {
                    if (profile.Address == serverAddress && profile.Port == serverPort)
                    {
                        Console.WriteLine($"[DEBUG] Found duplicate server: {profile.Remarks ?? profile.Address}");
                        return true;
                    }
                }
            }
            
            Console.WriteLine($"[DEBUG] Server is unique, safe to add");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error checking server duplicates: {ex.Message}");
            // If we can't check, allow the addition
            return false;
        }
    }

    private void HideHaioLoginDialog()
    {
        haioLoginDialog.IsVisible = false;
        DialogHost.Close(null);
    }

    private void ShowLoginStatus(string message, bool isError)
    {
        lblLoginStatus.Text = message;
        globalStatusBorder.IsVisible = true;
        
        if (isError)
        {
            globalStatusBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FEF2F2"));
            globalStatusBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444"));
            lblLoginStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DC2626"));
        }
        else
        {
            globalStatusBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F0FDF4"));
            globalStatusBorder.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22C55E"));
            lblLoginStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#16A34A"));
        }
        
        // Auto-hide success messages after 4 seconds
        if (!isError)
        {
            Task.Delay(4000).ContinueWith(_ => 
            {
                Dispatcher.UIThread.Post(() => globalStatusBorder.IsVisible = false);
            });
        }
    }

    private static bool IsValidMobileNumber(string mobile)
    {
        // Accept formats: +989123456789, 989123456789, 09123456789
        var pattern = @"^(\+98|98|0)?9\d{9}$";
        return Regex.IsMatch(mobile, pattern);
    }

    private static bool IsValidEmailAddress(string email)
    {
        var pattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        return Regex.IsMatch(email, pattern);
    }

    private async Task SetupHaioRoutingRules()
    {
        try
        {
            Console.WriteLine($"[DEBUG] Setting up HAIO domain-based routing rules...");
            
            // Read domains from domains.txt
            string domainsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "domains.txt");
            Console.WriteLine($"[DEBUG] Looking for domains file at: {domainsFilePath}");
            
            var domains = new List<string>();
            if (File.Exists(domainsFilePath))
            {
                var lines = await File.ReadAllLinesAsync(domainsFilePath);
                foreach (var line in lines)
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#"))
                    {
                        // Convert simple domain format to v2ray format
                        if (domain.StartsWith("."))
                        {
                            // .example.com -> domain:example.com (includes subdomains)
                            domains.Add($"domain:{domain.Substring(1)}");
                        }
                        else if (domain.Contains("keyword:"))
                        {
                            // keyword:youtube -> keyword:youtube (already correct)
                            domains.Add(domain);
                        }
                        else
                        {
                            // example.com -> full:example.com (exact match) and domain:example.com (includes subdomains)
                            domains.Add($"full:{domain}");
                            domains.Add($"domain:{domain}");
                        }
                    }
                }
                Console.WriteLine($"[DEBUG] Loaded {domains.Count} domain rules from domains.txt");
            }
            else
            {
                Console.WriteLine($"[DEBUG] domains.txt not found, using default blocked domains");
                // Add some default domains if file doesn't exist
                domains.AddRange(new[]
                {
                    "domain:twitter.com", "domain:youtube.com", "domain:facebook.com", 
                    "domain:instagram.com", "domain:telegram.org", "domain:google.com"
                });
            }

            // Create HAIO routing configuration
            await CreateHaioRoutingConfiguration(domains);
            
            Console.WriteLine($"[DEBUG] HAIO routing rules setup completed successfully");
            Logging.SaveLog($"HAIO domain-based routing configured with {domains.Count} domains");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error setting up HAIO routing rules: {ex.Message}");
            Logging.SaveLog($"Failed to setup HAIO routing: {ex.Message}");
        }
    }

    private async Task CreateHaioRoutingConfiguration(List<string> domains)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Creating HAIO routing configuration...");

            // Create routing rule set JSON for HAIO domains
            var haioRouteRules = new List<object>
            {
                // Route HAIO domains through proxy
                new
                {
                    remarks = "HAIO - Proxy domains from domains.txt",
                    outboundTag = "proxy",
                    domain = domains.ToArray()
                },
                // Block UDP 443 (common issue with some protocols)
                new
                {
                    remarks = "HAIO - Block UDP 443",
                    outboundTag = "block", 
                    port = "443",
                    network = "udp"
                },
                // Bypass private networks (local traffic)
                new
                {
                    remarks = "HAIO - Direct private networks",
                    outboundTag = "direct",
                    ip = new[] { "geoip:private" }
                },
                // Bypass all other domains (default direct)
                new
                {
                    remarks = "HAIO - Direct all other traffic",
                    outboundTag = "direct",
                    domain = new[] { "geosite:cn" }, // You can modify this based on your location
                    ip = new[] { "geoip:cn" }
                }
            };

            string routingJson = JsonSerializer.Serialize(haioRouteRules, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Console.WriteLine($"[DEBUG] Generated routing JSON: {routingJson}");

            // Use v2rayN's routing system to add the configuration
            // This integrates with the existing routing infrastructure
            var routingItem = new RoutingItem
            {
                Remarks = "HAIO Custom Domain Routing",
                RuleSet = routingJson,
                Sort = 1, // High priority
                IsActive = true
            };

            // Add the routing configuration using v2rayN's ConfigHandler
            await ConfigHandler.SaveRoutingItem(_config, routingItem);
            
            // Also activate this routing
            await ConfigHandler.SetDefaultRouting(_config, routingItem);
            
            Console.WriteLine($"[DEBUG] HAIO routing configuration added and activated successfully");
            Logging.SaveLog($"HAIO routing activated: {domains.Count} domains will be proxied");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error creating HAIO routing configuration: {ex.Message}");
            Logging.SaveLog($"Failed to create HAIO routing: {ex.Message}");
        }
    }

    private async void SaveAuthToken(string token)
    {
        try
        {
            var directory = Path.GetDirectoryName(_tokenFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory!);
            }
            
            await File.WriteAllTextAsync(_tokenFilePath, token);
            Console.WriteLine($"[DEBUG] Auth token saved successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Failed to save auth token: {ex.Message}");
        }
    }

    private async Task<string?> LoadAuthToken()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                var token = await File.ReadAllTextAsync(_tokenFilePath);
                Console.WriteLine($"[DEBUG] Auth token loaded successfully");
                return token;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Failed to load auth token: {ex.Message}");
        }
        
        return null;
    }

    private async Task<bool> ValidateAuthToken(string token)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Validating auth token with /v1/user/info");
            
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.haio.ir/v1/user/info");
            request.Headers.Add("Authorization", $"Bearer {token}");
            
            var response = await _httpClient.SendAsync(request);
            Console.WriteLine($"[DEBUG] Token validation response status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[DEBUG] Token validation response: {content}");
                
                var jsonDoc = JsonDocument.Parse(content);
                if (jsonDoc.RootElement.TryGetProperty("status", out var statusElement) && 
                    statusElement.GetBoolean())
                {
                    Console.WriteLine($"[DEBUG] Auth token is valid");
                    return true;
                }
            }
            
            Console.WriteLine($"[DEBUG] Auth token is invalid or expired");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error validating auth token: {ex.Message}");
            return false;
        }
    }

    private void DeleteAuthToken()
    {
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
                Console.WriteLine($"[DEBUG] Auth token deleted");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Failed to delete auth token: {ex.Message}");
        }
    }

    // Keyboard event handlers for better UX
    private void TxtOtpMobile_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnRequestOtp_Click(sender, new RoutedEventArgs());
        }
    }

    private void TxtOtpCode_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && btnLogin.IsEnabled)
        {
            BtnLogin_Click(sender, new RoutedEventArgs());
        }
    }

    private void TxtPasswordEmail_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            // Move focus to password field
            txtPassword.Focus();
        }
    }

    private void TxtPassword_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            BtnLoginPassword_Click(sender, new RoutedEventArgs());
        }
    }

    #endregion HAIO Authentication
}
