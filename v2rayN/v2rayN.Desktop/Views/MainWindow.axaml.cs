using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Controls.Primitives;
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
using ServiceLib.Handler.SysProxy;
using ServiceLib.ViewModels;
using ServiceLib.Enums;
using ServiceLib.Common;
using System.IO;
using System.Text;

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
    // Log tailing
    private FileSystemWatcher? _logWatcher;
    private string? _currentLogFile;
    private readonly StringBuilder _logBuffer = new();
    private const int MaxLogChars = 200_000; // ~200 KB in textbox
    
    // User-visible logging system
    private static readonly List<string> _userLogs = new();
    private static readonly object _userLogsLock = new();

    public MainWindow()
    {
        InitializeComponent();

        _config = AppManager.Instance.Config;
        _manager = new WindowNotificationManager(TopLevel.GetTopLevel(this)) { MaxItems = 3, Position = NotificationPosition.TopRight };

        // Initialize HTTP client with direct connection (no proxy)
        var handler = new HttpClientHandler();
        handler.UseProxy = false;
        _httpClient = new HttpClient(handler);

        // Simplified event handlers for single button interface
        this.KeyDown += MainWindow_KeyDown;
        btnToggleAntiSanction.IsCheckedChanged += BtnToggleAntiSanction_Click;
        btnCloseApp.Click += BtnCloseApp_Click;
        
        // Configuration management event handlers
        btnRefreshConfigs.Click += BtnRefreshConfigs_Click;
        cmbConfigurations.SelectionChanged += CmbConfigurations_SelectionChanged;

        // HAIO Login Event Handlers
        btnRequestOtp.Click += BtnRequestOtp_Click;
        btnLogin.Click += BtnLogin_Click;
        btnLoginPassword.Click += BtnLoginPassword_Click;
        btnSkipLogin.Click += BtnSkipLogin_Click;

        ViewModel = new MainWindowViewModel(UpdateViewHandler);
        Locator.CurrentMutable.RegisterLazySingleton(() => ViewModel, typeof(MainWindowViewModel));

        // Show HAIO login dialog after window is loaded
        this.Loaded += MainWindow_Loaded;
    this.Closed += MainWindow_Closed;
        
        // Subscribe to ServiceLib events for user-visible logging
        SubscribeToAppEvents();

        this.WhenActivated(disposables =>
        {
            // Simplified bindings for single button interface - no complex menu system
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

        this.Title = "HAIO Anti-Sanction";

        if (Utils.IsWindows())
        {
            ThreadPool.RegisterWaitForSingleObject(Program.ProgramStarted, OnProgramStarted, null, -1, false);
            // Removed hotkey manager for simplified interface
        }

        // Initialize UI state and load configurations
        UpdateUIState(false); // Start with anti-sanction OFF
        LoadConfigurations(); // Load available proxy configurations

        // Wire log buttons
        btnClearLogs.Click += (_, __) => { txtLogs.Text = string.Empty; _logBuffer.Clear(); };
        btnCopyLogs.Click += async (_, __) =>
        {
            try
            {
                var top = TopLevel.GetTopLevel(this);
                if (top?.Clipboard != null)
                {
                    await top.Clipboard.SetTextAsync(txtLogs.Text ?? string.Empty);
                }
            }
            catch { }
        };

        // Start log tailing
        InitLogTailing();
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
            else if (e.Key == Key.F12)
            {
                // Show debug logs
                ShowDebugLogsDialog();
            }
            else if (e.Key == Key.Escape)
            {
                // Close any open dialogs
                HideHaioLoginDialog();
                globalStatusBorder.IsVisible = false;
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
        // Simplified interface doesn't need grid layout restoration
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
        // Simplified interface doesn't need to store grid layout
    }

    // Simplified interface - no help menu needed

    #endregion UI

    #region HAIO Authentication

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        // Check for existing auth token first
        await Task.Delay(1000); // Longer delay to ensure DialogHost is ready
        await CheckExistingAuthToken();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        try
        {
            if (_logWatcher is not null)
            {
                _logWatcher.EnableRaisingEvents = false;
                _logWatcher.Created -= OnLogFileChanged;
                _logWatcher.Changed -= OnLogFileChanged;
                _logWatcher.Dispose();
                _logWatcher = null;
            }
        }
        catch { }
    }

    private void InitLogTailing()
    {
        try
        {
            var logDir = Utils.GetLogPath();
            Directory.CreateDirectory(logDir);
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            _currentLogFile = Path.Combine(logDir, today + ".txt");

            // Seed with last lines if exists
            if (File.Exists(_currentLogFile))
            {
                var seeded = TailRead(_currentLogFile, 500);
                AppendLogs(seeded);
            }

            _logWatcher = new FileSystemWatcher(logDir)
            {
                IncludeSubdirectories = false,
                Filter = "*.txt",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
            };
            _logWatcher.Created += OnLogFileChanged;
            _logWatcher.Changed += OnLogFileChanged;
            _logWatcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Log tail init failed: {ex.Message}");
        }
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Switch to new day file if needed
            if (_currentLogFile == null || !e.FullPath.Equals(_currentLogFile, StringComparison.OrdinalIgnoreCase))
            {
                var today = DateTime.Now.ToString("yyyy-MM-dd");
                var expected = Path.Combine(Path.GetDirectoryName(e.FullPath)!, today + ".txt");
                _currentLogFile = expected;
            }

            // Read appended text safely
            if (_currentLogFile != null && File.Exists(_currentLogFile))
            {
                var chunk = SafeReadTailChunk(_currentLogFile, 8192);
                if (!string.IsNullOrEmpty(chunk))
                {
                    AppendLogs(chunk);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Log change handling failed: {ex.Message}");
        }
    }

    private void AppendLogs(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        lock (_userLogsLock)
        {
            _logBuffer.Append(text);
            if (_logBuffer.Length > MaxLogChars)
            {
                _logBuffer.Remove(0, _logBuffer.Length - MaxLogChars);
            }
            var toSet = _logBuffer.ToString();
            Dispatcher.UIThread.Post(() =>
            {
                txtLogs.Text = toSet;
                // Auto-scroll to bottom
                scrollLogs.Offset = new Avalonia.Vector(scrollLogs.Offset.X, double.MaxValue);
            });
        }
    }

    private static string TailRead(string path, int lineCount)
    {
        try
        {
            var lines = new LinkedList<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (line is null) break;
                lines.AddLast(line);
                if (lines.Count > lineCount) lines.RemoveFirst();
            }
            return string.Join('\n', lines) + '\n';
        }
        catch { return string.Empty; }
    }

    private static string SafeReadTailChunk(string path, int maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            var start = Math.Max(0, len - maxBytes);
            fs.Position = start;
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return string.Empty; }
    }

    private async Task CheckExistingAuthToken()
    {
        try
        {
            // Check for bypass file first
            var bypassFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bypass_auth.txt");
            if (File.Exists(bypassFilePath))
            {
                Console.WriteLine($"[DEBUG] Found bypass_auth.txt file, skipping authentication");
                Logging.SaveLog("Authentication bypassed via bypass_auth.txt file");
                ShowLoginStatus("Development mode - Authentication bypassed", isError: false);
                return; // Don't show login dialog
            }

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
        await ShowHaioLoginDialogSafe();
    }

    private async Task ShowHaioLoginDialogSafe()
    {
        try
        {
            // Ensure UI is ready before showing dialog
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // Give the UI a moment to fully load
                await Task.Delay(100);
                DialogHost.Show(haioLoginDialog);
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error showing login dialog: {ex.Message}");
            // Fallback: try again after a longer delay
            await Task.Delay(1000);
            try
            {
                DialogHost.Show(haioLoginDialog);
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[DEBUG] Failed to show login dialog after retry: {ex2.Message}");
            }
        }
    }

    private void ShowHaioLoginDialog()
    {
        // Legacy sync method - calls the safe async version
        _ = ShowHaioLoginDialogSafe();
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
            
            // Now reload so the core picks up the new HAIO routing immediately
            Console.WriteLine($"[DEBUG] Reloading core to apply HAIO routing...");
            await ViewModel.Reload();
            
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
                // Do not reload here; we'll reload after HAIO routing is created
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
                    
                    // Get additional info for logging and display
                    string? title = package.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : "Unknown";
                    string? domain = package.TryGetProperty("domain", out var domainElement) ? domainElement.GetString() : "Unknown";
                    bool isOver = package.TryGetProperty("is_over", out var isOverElement) && isOverElement.GetInt32() == 1;
                    
                    // Get traffic information
                    double totalTraffic = package.TryGetProperty("total_traffic", out var totalTrafficElement) ? totalTrafficElement.GetDouble() : 0;
                    string totalTrafficUsage = package.TryGetProperty("total_traffic_usage", out var totalTrafficUsageElement) ? totalTrafficUsageElement.GetString() ?? "0.00" : "0.00";
                    string usagePercent = package.TryGetProperty("usage_percent", out var usagePercentElement) ? usagePercentElement.GetString() ?? "0.00" : "0.00";
                    
                    // Clean up the title (remove newlines and whitespace)
                    string cleanTitle = title?.Trim().Replace("\n", "").Replace("\r", "") ?? "Unknown Server";
                    if (string.IsNullOrWhiteSpace(cleanTitle) || cleanTitle.ToLower() == "server")
                    {
                        cleanTitle = $"HAIO-{domain?.Split('.').FirstOrDefault() ?? "Server"}";
                    }
                    
                    // Create display name with traffic info
                    string displayName = $"{cleanTitle} ({totalTraffic}GB, {usagePercent}% used)";
                    if (isOver)
                    {
                        displayName += " [EXPIRED]";
                    }
                    
                    Console.WriteLine($"[DEBUG] Server: {cleanTitle}, Domain: {domain}, Is Expired: {isOver}");
                    Console.WriteLine($"[DEBUG] Traffic: {totalTraffic}GB total, {totalTrafficUsage}GB used ({usagePercent}%)");
                    
                    // Only add active servers (hide expired ones from UI)
                    if (!isOver)
                    {
                        await AddTrojanConfigurationFromUrl(configUrl, displayName);
                    }
                    else
                    {
                        Console.WriteLine($"[DEBUG] Skipping expired server from UI: {cleanTitle}");
                        Logging.SaveLog($"Skipped expired HAIO server from UI: {cleanTitle} ({domain})");
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

            // Use existing method to add server via clipboard: inject the desired remarks as URL fragment
            // If the URL already contains a fragment, replace it; otherwise, append it
            string urlWithRemark;
            try
            {
                var uri = new Uri(configUrl);
                var baseNoFragment = configUrl.Split('#')[0];
                var encoded = Utils.UrlEncode(serverName ?? "HAIO");
                urlWithRemark = $"{baseNoFragment}#{encoded}";
            }
            catch
            {
                // Fallback: simple append
                var encoded = Utils.UrlEncode(serverName ?? "HAIO");
                urlWithRemark = configUrl.Contains('#') ? Regex.Replace(configUrl, "#.*$", "#" + encoded) : configUrl + "#" + encoded;
            }

            await ViewModel.AddServerViaClipboardAsync(urlWithRemark);

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
            var serverUser = uri.UserInfo; // This contains the password/username which makes each HAIO server unique
            
            Console.WriteLine($"[DEBUG] Checking for existing server: {serverAddress}:{serverPort} with user {serverUser}");
            
            // Get all existing profile items
            var existingProfiles = await AppManager.Instance.ProfileItems("");
            
            if (existingProfiles != null)
            {
                // Check if any existing server matches this address, port, AND user credentials
                foreach (var profile in existingProfiles)
                {
                    if (profile.Address == serverAddress && profile.Port == serverPort && profile.Id == serverUser)
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
        try
        {
            haioLoginDialog.IsVisible = false;
            // DialogHost.Close throws if no dialog is open; guard with try/catch
            DialogHost.Close(null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] HideHaioLoginDialog: {ex.Message}");
        }
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

            var proxyDomains = new List<string>();

            // Download domains.txt from tools.haiocloud.com if it doesn't exist
            if (!File.Exists(domainsFilePath))
            {
                Console.WriteLine($"[DEBUG] domains.txt not found, downloading from tools.haiocloud.com...");
                await DownloadDomainsFile(domainsFilePath);
            }

            if (File.Exists(domainsFilePath))
            {
                var lines = await File.ReadAllLinesAsync(domainsFilePath);
                foreach (var line in lines)
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#"))
                    {
                        // Only add valid domains
                        if (domain.StartsWith("."))
                        {
                            proxyDomains.Add($"domain:{domain.Substring(1)}");
                        }
                        else if (domain.Contains("keyword:"))
                        {
                            proxyDomains.Add(domain);
                        }
                        else
                        {
                            proxyDomains.Add($"full:{domain}");
                            proxyDomains.Add($"domain:{domain}");
                        }
                    }
                }
                Console.WriteLine($"[DEBUG] Loaded {proxyDomains.Count} domain rules from domains.txt");
            }
            else
            {
                Console.WriteLine($"[DEBUG] Failed to get domains.txt, using default blocked domains");
                proxyDomains.AddRange(new[]
                {
                    "domain:twitter.com", "domain:youtube.com", "domain:facebook.com",
                    "domain:instagram.com", "domain:telegram.org", "domain:google.com"
                });
            }

            // Create HAIO routing configuration
            await CreateHaioRoutingConfiguration(proxyDomains);

            Console.WriteLine($"[DEBUG] HAIO routing rules setup completed successfully");
            Logging.SaveLog($"HAIO domain-based routing configured with {proxyDomains.Count} domains");
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

            // Use proxy port 10820 for PAC/routing
            int proxyPort = 10820;

            // Create routing rule set JSON for HAIO domains
            var haioRouteRules = new List<object>
            {
                // Route HAIO domains through proxy
                new
                {
                    remarks = $"HAIO - Proxy domains from domains.txt (port {proxyPort})",
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
                // Direct all other traffic (everything not matching above rules)
                new
                {
                    remarks = "HAIO - Direct all other traffic",
                    outboundTag = "direct",
                    network = "tcp,udp"
                    // No domain or ip filters - this catches everything else
                }
            };

            string routingJson = JsonSerializer.Serialize(haioRouteRules, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            Console.WriteLine($"[DEBUG] Generated routing JSON: {routingJson}");

            // Clear ALL existing routing rules first
            Console.WriteLine($"[DEBUG] Clearing all existing routing rules...");
            var existingRoutings = await AppManager.Instance.RoutingItems();
            if (existingRoutings != null)
            {
                foreach (var existingRoute in existingRoutings.ToList())
                {
                    Console.WriteLine($"[DEBUG] Removing existing routing rule: {existingRoute.Remarks}");
                    await ConfigHandler.RemoveRoutingItem(existingRoute);
                }
            }

            // Create only our HAIO routing rule
            var haioRouting = new RoutingItem
            {
                Remarks = $"HAIO Custom Domain Routing (port {proxyPort})",
                RuleSet = routingJson,
                Sort = 1,
                IsActive = true
            };

            await ConfigHandler.SaveRoutingItem(_config, haioRouting);
            await ConfigHandler.SetDefaultRouting(_config, haioRouting);

            Console.WriteLine($"[DEBUG] HAIO routing configuration added and activated successfully");
            Logging.SaveLog($"HAIO routing activated: {domains.Count} domains will be proxied on port {proxyPort}");
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

    private async Task DownloadDomainsFile(string filePath)
    {
        try
        {
            Console.WriteLine($"[DEBUG] Downloading domains.txt from tools.haiocloud.com...");
            
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://tools.haiocloud.com/domains.txt");
            var response = await _httpClient.SendAsync(request);
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(filePath, content);
                Console.WriteLine($"[DEBUG] Successfully downloaded domains.txt ({content.Length} chars)");
                Logging.SaveLog("Downloaded domains.txt from tools.haiocloud.com");
            }
            else
            {
                Console.WriteLine($"[DEBUG] Failed to download domains.txt: {response.StatusCode}");
                Logging.SaveLog($"Failed to download domains.txt: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error downloading domains.txt: {ex.Message}");
            Logging.SaveLog($"Error downloading domains.txt: {ex.Message}");
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

    #region Simplified UI Event Handlers

    private async void BtnToggleAntiSanction_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button) return;

        try
        {
            var statusBarViewModel = Locator.Current.GetService<StatusBarViewModel>();
            if (statusBarViewModel == null) return;

            bool isActivating = button.IsChecked == true;
            
            if (isActivating)
            {
                // Check if a configuration is selected
                if (cmbConfigurations.SelectedItem is not ProfileItemModel selectedConfig)
                {
                    LogToUser("PROXY", "No configuration selected - please select one first");
                    _manager?.Show(new Notification(null, "⚠️ Please select a configuration first", NotificationType.Warning));
                    button.IsChecked = false; // Revert button state
                    return;
                }

                LogToUser("PROXY", $"Activating Anti-Sanction via {selectedConfig.Remarks} ({selectedConfig.Address}:{selectedConfig.Port})");

                // Ensure the selected configuration is set as default
                await ConfigHandler.SetDefaultServerIndex(_config, selectedConfig.IndexId);
                
                // Do NOT enable PAC – keep system proxy unchanged per user preference
                UpdateUIState(true);
                LogToUser("PROXY", "Anti-Sanction activated (system proxy unchanged; no PAC)");
                _manager?.Show(new Notification(null, $"🟢 Connected via {selectedConfig.Remarks}!", NotificationType.Success));
            }
            else
            {
                LogToUser("PROXY", "Deactivating Anti-Sanction proxy");
                
                // Deactivate Anti-Sanction – keep system proxy unchanged (no auto-clear)
                UpdateUIState(false);
                
                // Explicitly clear system proxy settings at OS level
                try
                {
                    await SysProxyHandler.UpdateSysProxy(_config, true);
                    LogToUser("PROXY", "System proxy cleared at OS level");
                }
                catch (Exception clearEx)
                {
                    LogToUser("ERROR", $"Failed to clear system proxy: {clearEx.Message}");
                }
                
                LogToUser("PROXY", "Anti-Sanction deactivated - proxy cleared");
                _manager?.Show(new Notification(null, "🔴 Anti-Sanction deactivated", NotificationType.Information));
            }
        }
        catch (Exception ex)
        {
            _manager?.Show(new Notification(null, $"❌ Error: {ex.Message}", NotificationType.Error));
            // Revert button state on error
            if (sender is ToggleButton btn)
                btn.IsChecked = !btn.IsChecked;
        }
    }

    private void BtnCloseApp_Click(object? sender, RoutedEventArgs e)
    {
        _blCloseByUser = true;
        StorageUI();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void UpdateUIState(bool isActive)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (isActive)
            {
                txtConnectionStatus.Text = "🟢 Anti-Sanction is ON";
                txtConnectionStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(40, 167, 69));
                
                // Show information about selected configuration
                if (cmbConfigurations.SelectedItem is ProfileItemModel selectedConfig)
                {
                    txtStatusDescription.Text = $"Connected via {selectedConfig.ConfigTypeDisplay}: {selectedConfig.Remarks}";
                    txtServerInfo.Text = $"Active: {selectedConfig.Address}:{selectedConfig.Port}";
                }
                else
                {
                    txtStatusDescription.Text = "Your connection is protected through secure proxy";
                    txtServerInfo.Text = "PAC Mode Active - Automatic server selection";
                }
            }
            else
            {
                txtConnectionStatus.Text = "🔴 Anti-Sanction is OFF";
                txtConnectionStatus.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(220, 53, 69));
                
                // Show information about selected configuration in ready state
                if (cmbConfigurations.SelectedItem is ProfileItemModel selectedConfig)
                {
                    txtStatusDescription.Text = $"Ready to activate {selectedConfig.ConfigTypeDisplay}: {selectedConfig.Remarks}";
                    txtServerInfo.Text = $"Selected: {selectedConfig.Address}:{selectedConfig.Port}";
                }
                else
                {
                    txtStatusDescription.Text = "Select a configuration and click to activate secure proxy protection";
                    txtServerInfo.Text = "No configuration selected";
                }
                
                txtSpeedInfo.Text = "";
            }
        });
    }

    #endregion Simplified UI Event Handlers

    #region Configuration Management

    private async void LoadConfigurations()
    {
        try
        {
            Console.WriteLine("[DEBUG] Loading proxy configurations...");
            
            // Get all profile items from the configuration
            var profileItems = await AppManager.Instance.ProfileItems("", "");
            if (profileItems == null || profileItems.Count == 0)
            {
                Console.WriteLine("[DEBUG] No configurations found");
                UpdateConfigurationUI(new List<ProfileItemModel>());
                return;
            }

            // ProfileItems already returns ProfileItemModel, just update the ConfigTypeDisplay
            foreach (var profile in profileItems)
            {
                profile.ConfigTypeDisplay = GetConfigTypeDisplay(profile.ConfigType);
            }

            Console.WriteLine($"[DEBUG] Loaded {profileItems.Count} configurations");
            UpdateConfigurationUI(profileItems);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error loading configurations: {ex.Message}");
            _manager?.Show(new Notification(null, $"❌ Error loading configurations: {ex.Message}", NotificationType.Error));
            UpdateConfigurationUI(new List<ProfileItemModel>());
        }
    }

    private void UpdateConfigurationUI(List<ProfileItemModel> configurations)
    {
        Dispatcher.UIThread.Post(() =>
        {
            cmbConfigurations.ItemsSource = configurations;
            
            if (configurations.Count > 0)
            {
                txtConfigCount.Text = $"{configurations.Count} configuration(s) available";
                
                // Select the currently active configuration if any
                var activeConfigId = _config.IndexId;
                if (!string.IsNullOrEmpty(activeConfigId))
                {
                    var activeConfig = configurations.FirstOrDefault(c => c.IndexId == activeConfigId);
                    if (activeConfig != null)
                    {
                        cmbConfigurations.SelectedItem = activeConfig;
                    }
                }
                
                // If nothing is selected, select the first item
                if (cmbConfigurations.SelectedItem == null && configurations.Count > 0)
                {
                    cmbConfigurations.SelectedIndex = 0;
                }
            }
            else
            {
                txtConfigCount.Text = "No configurations available";
                txtConfigCount.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#DC3545"));
            }
        });
    }

    private string GetConfigTypeDisplay(EConfigType configType)
    {
        return configType switch
        {
            EConfigType.VMess => "VMess",
            EConfigType.Shadowsocks => "SS",
            EConfigType.SOCKS => "SOCKS",
            EConfigType.Trojan => "Trojan",
            EConfigType.VLESS => "VLESS",
            EConfigType.Hysteria2 => "Hy2",
            EConfigType.TUIC => "TUIC",
            EConfigType.WireGuard => "WG",
            EConfigType.HTTP => "HTTP",
            EConfigType.Anytls => "AnyTLS",
            EConfigType.Custom => "Custom",
            _ => configType.ToString()
        };
    }

    private async void BtnRefreshConfigs_Click(object? sender, RoutedEventArgs e)
    {
        btnRefreshConfigs.IsEnabled = false;
        btnRefreshConfigs.Content = "🔄 Loading...";
        
        try
        {
            await Task.Delay(500); // Small delay for visual feedback
            LoadConfigurations();
            _manager?.Show(new Notification(null, "✅ Configurations refreshed", NotificationType.Success));
        }
        catch (Exception ex)
        {
            _manager?.Show(new Notification(null, $"❌ Error refreshing: {ex.Message}", NotificationType.Error));
        }
        finally
        {
            btnRefreshConfigs.IsEnabled = true;
            btnRefreshConfigs.Content = "🔄 Refresh";
        }
    }

    private async void CmbConfigurations_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cmbConfigurations.SelectedItem is not ProfileItemModel selectedConfig)
        {
            return;
        }

        try
        {
            Console.WriteLine($"[DEBUG] Configuration selected: {selectedConfig.Remarks} ({selectedConfig.Address}:{selectedConfig.Port})");
            
            // Set this configuration as the default server
            await ConfigHandler.SetDefaultServerIndex(_config, selectedConfig.IndexId);
            
            // Update the UI to reflect the selected configuration
            UpdateSelectedConfigurationInfo(selectedConfig);
            
            Console.WriteLine($"[DEBUG] Default server updated to: {selectedConfig.IndexId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error selecting configuration: {ex.Message}");
            _manager?.Show(new Notification(null, $"❌ Error selecting configuration: {ex.Message}", NotificationType.Error));
        }
    }

    private void UpdateSelectedConfigurationInfo(ProfileItemModel config)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (config != null)
            {
                txtServerInfo.Text = $"Selected: {config.Remarks} - {config.Address}:{config.Port}";
                
                // Update status description to show selected config
                if (btnToggleAntiSanction.IsChecked == true)
                {
                    txtStatusDescription.Text = $"Connected via {config.ConfigTypeDisplay}: {config.Remarks}";
                }
                else
                {
                    txtStatusDescription.Text = $"Ready to activate {config.ConfigTypeDisplay}: {config.Remarks}";
                }
            }
        });
    }

    #endregion Configuration Management

    #region User-Visible Logging System

    // Static method to log messages that users can see
    public static void LogToUser(string category, string message)
    {
        lock (_userLogsLock)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] [{category}] {message}";
            _userLogs.Add(logEntry);
            
            // Keep only last 100 log entries
            if (_userLogs.Count > 100)
            {
                _userLogs.RemoveAt(0);
            }
            
            // Also write to regular log
            Logging.SaveLog(logEntry);
            
            // Show notification for important messages
            if (category == "ERROR")
            {
                ShowUserNotification($"❌ {message}", NotificationType.Error);
            }
            else if (category == "AUTH" || category == "PROXY")
            {
                ShowUserNotification($"ℹ️ {message}", NotificationType.Information);
            }
        }
    }
    
    // Subscribe to ServiceLib notifications to capture proxy operations
    private void SubscribeToAppEvents()
    {
        // Subscribe to snack messages (brief notifications) - these are proxy status updates
        ServiceLib.Handler.AppEvents.SendSnackMsgRequested.Subscribe(message =>
        {
            // Check if it's a proxy-related message and log appropriately
            if (message.Contains("Anti-Sanction") || message.Contains("proxy") || message.Contains("PAC"))
            {
                LogToUser("PROXY", message);
            }
            else
            {
                LogToUser("SYSTEM", message);
            }
        });

        // Subscribe to detailed messages (operations) - these are detailed status info
        ServiceLib.Handler.AppEvents.SendMsgViewRequested.Subscribe(message =>
        {
            LogToUser("INFO", message);
        });
    }

    private static void ShowUserNotification(string message, NotificationType type)
    {
        try
        {
            // Find the main window instance to show notifications
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainWindow = desktop.MainWindow as MainWindow;
                Dispatcher.UIThread.Post(() => 
                {
                    mainWindow?._manager?.Show(new Notification(null, message, type));
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] Error showing user notification: {ex.Message}");
        }
    }

    private void ShowDebugLogsDialog()
    {
        try
        {
            List<string> logsCopy;
            lock (_userLogsLock)
            {
                logsCopy = new List<string>(_userLogs);
            }
            
            if (logsCopy.Count == 0)
            {
                logsCopy.Add("[No debug logs available yet]");
                logsCopy.Add("Logs will appear here when you use proxy functions:");
                logsCopy.Add("- Toggle Anti-Sanction ON/OFF");
                logsCopy.Add("- Change proxy configurations");
                logsCopy.Add("- Authentication events");
            }

            var logsText = string.Join("\n", logsCopy);
            
            var dialog = new Window
            {
                Title = "Debug Logs (F12 to show, Escape to close)",
                Width = 800,
                Height = 600,
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = logsText,
                        FontFamily = "Consolas,Monaco,monospace",
                        FontSize = 12,
                        Margin = new Thickness(10),
                        TextWrapping = Avalonia.Media.TextWrapping.NoWrap
                    }
                }
            };

            // Add key handlers to close with Escape
            dialog.KeyDown += (sender, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    dialog.Close();
                }
            };

            dialog.ShowDialog(this);
        }
        catch (Exception ex)
        {
            _manager?.Show(new Notification(null, $"❌ Error showing logs: {ex.Message}", NotificationType.Error));
        }
    }

    #endregion User-Visible Logging System
}
