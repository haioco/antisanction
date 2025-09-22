using ServiceLib.Manager;

namespace ServiceLib.Handler.SysProxy;

public static class SysProxyHandler
{
    private static readonly string _tag = "SysProxyHandler";

    public static async Task<bool> UpdateSysProxy(Config config, bool forceDisable)
    {
        var type = config.SystemProxyItem.SysProxyType;

        Logging.SaveLog($"UpdateSysProxy called - Original type: {type}, ForceDisable: {forceDisable}, OS: {Environment.OSVersion}");
        NoticeManager.Instance.SendMessage($"System proxy update: {type} (Force disable: {forceDisable})");

        // When force disabling, always clear the system proxy regardless of current type
        if (forceDisable)
        {
            type = ESysProxyType.ForcedClear;
            Logging.SaveLog($"Force disable requested - Type changed to: {type}");
            NoticeManager.Instance.SendMessage("Force clearing system proxy");
        }

        try
        {
            // Prepare exceptions safely (may be null in config) 
            var exceptions = (config.SystemProxyItem.SystemProxyExceptions ?? string.Empty).Replace(" ", "");
            
            Logging.SaveLog($"Processing proxy type: {type}");
            
            switch (type)
            {
                case ESysProxyType.ForcedChange when Utils.IsWindows():
                    {
                        // Use mixed port for system proxy (supports both HTTP and SOCKS)
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for ForcedChange on Windows - returning false");
                            return false;
                        }
                        Logging.SaveLog("Setting Windows manual proxy");
                        GetWindowsProxyString(config, port, out var strProxy, out var strExceptions);
                        Logging.SaveLog($"Windows proxy string: '{strProxy}', exceptions: '{strExceptions}'");
                        ProxySettingWindows.SetProxy(strProxy, strExceptions, 2);
                        Logging.SaveLog("Windows manual proxy set successfully");
                        break;
                    }
                case ESysProxyType.ForcedChange when Utils.IsLinux():
                    {
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for ForcedChange on Linux - returning false");
                            return false;
                        }
                        Logging.SaveLog($"Setting Linux manual proxy - Host: {Global.Loopback}, Port: {port}");
                    NoticeManager.Instance.SendMessage($"Setting Linux manual proxy on port {port}");
                    await ProxySettingLinux.SetProxy(Global.Loopback, port, exceptions);
                    Logging.SaveLog("Linux manual proxy set successfully");
                    NoticeManager.Instance.Enqueue("Linux manual proxy activated");
                    break;
                    }

                case ESysProxyType.ForcedChange when Utils.IsOSX():
                    {
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for ForcedChange on OSX - returning false");
                            return false;
                        }
                        Logging.SaveLog($"Setting OSX manual proxy - Host: {Global.Loopback}, Port: {port}");
                        await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions);
                        Logging.SaveLog("OSX manual proxy set successfully");
                        break;
                    }

                case ESysProxyType.ForcedClear when Utils.IsWindows():
                    Logging.SaveLog("Clearing Windows proxy");
                    ProxySettingWindows.UnsetProxy();
                    Logging.SaveLog("Windows proxy cleared successfully");
                    break;

                case ESysProxyType.ForcedClear when Utils.IsLinux():
                    Logging.SaveLog("Clearing Linux proxy");
                    NoticeManager.Instance.SendMessage("Clearing Linux system proxy");
                    await ProxySettingLinux.UnsetProxy();
                    Logging.SaveLog("Linux proxy cleared successfully");
                    NoticeManager.Instance.Enqueue("Linux proxy cleared");
                    break;

                case ESysProxyType.ForcedClear when Utils.IsOSX():
                    Logging.SaveLog("Clearing OSX proxy");
                    await ProxySettingOSX.UnsetProxy();
                    Logging.SaveLog("OSX proxy cleared successfully");
                    break;

                case ESysProxyType.Pac when Utils.IsWindows():
                    {
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for PAC on Windows - returning false");
                            return false;
                        }
                        
                        // Create HAIO PAC file with domain-specific routing
                        Logging.SaveLog("Creating HAIO PAC file for Windows with domain-specific routing");
                        NoticeManager.Instance.SendMessage("Setting up domain-specific PAC proxy");
                        
                        var pacContent = await CreateHaioPacContent(Global.Loopback, port);
                        var pacPath = Path.Combine(Utils.GetConfigPath(), "haio-proxy.pac");
                        await File.WriteAllTextAsync(pacPath, pacContent);
                        
                        var pacUrl = $"file:///{pacPath.Replace('\\', '/')}";
                        Logging.SaveLog($"PAC file created at: {pacPath}");
                        Logging.SaveLog($"PAC URL: {pacUrl}");
                        
                        ProxySettingWindows.SetProxy(pacUrl, "", 4); // 4 = PAC mode
                        Logging.SaveLog("Windows PAC proxy set successfully with domain-specific routing");
                        NoticeManager.Instance.Enqueue("Domain-specific PAC proxy activated");
                        break;
                    }

                case ESysProxyType.Pac when Utils.IsLinux():
                    {
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for PAC on Linux - returning false");
                            return false;
                        }
                        
                        // Create HAIO PAC file with domain-specific routing
                        Logging.SaveLog("Creating HAIO PAC file for Linux with domain-specific routing");
                        NoticeManager.Instance.SendMessage("Setting up domain-specific PAC proxy");
                        
                        var pacContent = await CreateHaioPacContent(Global.Loopback, port);
                        var pacPath = Path.Combine(Utils.GetConfigPath(), "haio-proxy.pac");
                        await File.WriteAllTextAsync(pacPath, pacContent);
                        
                        var pacUrl = $"file://{pacPath}";
                        Logging.SaveLog($"PAC file created at: {pacPath}");
                        Logging.SaveLog($"PAC URL: {pacUrl}");
                        
                        await ProxySettingLinux.SetPacProxy(pacUrl);
                        Logging.SaveLog("Linux PAC proxy set successfully with domain-specific routing");
                        NoticeManager.Instance.Enqueue("Domain-specific PAC proxy activated");
                        break;
                    }

                case ESysProxyType.Pac when Utils.IsOSX():
                    {
                        var port = AppManager.Instance.GetLocalPort(EInboundProtocol.mixed);
                        if (port <= 0)
                        {
                            Logging.SaveLog($"ERROR: Invalid port {port} for PAC on OSX - returning false");
                            return false;
                        }
                        Logging.SaveLog($"Setting OSX direct proxy (instead of PAC) - Host: {Global.Loopback}, Port: {port}");
                        await ProxySettingOSX.SetProxy(Global.Loopback, port, exceptions);
                        Logging.SaveLog("OSX direct proxy set successfully");
                        break;
                    }
                    
                default:
                    Logging.SaveLog($"No matching case for type: {type} on OS: {Environment.OSVersion}");
                    break;
            }

            // Always stop PAC server since we're using direct proxy instead of PAC
            PacManager.Instance.Stop();
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ERROR in UpdateSysProxy: {ex.Message}");
            Logging.SaveLog($"Exception details: {ex}");
            return false;
        }
        
        Logging.SaveLog("UpdateSysProxy completed successfully");
        return true;
    }

    private static void GetWindowsProxyString(Config config, int port, out string strProxy, out string strExceptions)
    {
        strExceptions = (config.SystemProxyItem.SystemProxyExceptions ?? string.Empty).Replace(" ", "");
        if (config.SystemProxyItem.NotProxyLocalAddress)
        {
            strExceptions = $"<local>;{strExceptions}";
        }

        strProxy = string.Empty;
        if (config.SystemProxyItem.SystemProxyAdvancedProtocol.IsNullOrEmpty())
        {
            strProxy = $"{Global.Loopback}:{port}";
        }
        else
        {
            strProxy = config.SystemProxyItem.SystemProxyAdvancedProtocol
                .Replace("{ip}", Global.Loopback)
                .Replace("{http_port}", port.ToString())
                .Replace("{socks_port}", port.ToString());
        }
    }

    private static async Task SetWindowsProxyPac(int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        await PacManager.Instance.StartAsync(Utils.GetConfigPath(), port, portPac);
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        ProxySettingWindows.SetProxy(strProxy, "", 4);
    }

    private static async Task SetLinuxProxyPac(int port)
    {
        Logging.SaveLog($"SetLinuxProxyPac starting with port: {port}");
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        Logging.SaveLog($"PAC port retrieved: {portPac}");
        
        await PacManager.Instance.StartAsync(Utils.GetConfigPath(), port, portPac);
        Logging.SaveLog("PAC Manager started successfully");
        
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        Logging.SaveLog($"Generated PAC URL: {strProxy}");
        
        await ProxySettingLinux.SetPacProxy(strProxy);
        Logging.SaveLog("Linux PAC proxy configured successfully");
    }

    private static async Task SetOSXProxyPac(int port)
    {
        var portPac = AppManager.Instance.GetLocalPort(EInboundProtocol.pac);
        await PacManager.Instance.StartAsync(Utils.GetConfigPath(), port, portPac);
        var strProxy = $"{Global.HttpProtocol}{Global.Loopback}:{portPac}/pac?t={DateTime.Now.Ticks}";
        await ProxySettingOSX.SetPacProxy(strProxy);
    }

    private static async Task<string> CreateHaioPacContent(string proxyHost, int proxyPort)
    {
        try
        {
            var domainsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "domains.txt");
            var proxyDomains = new Dictionary<string, bool>();
            
            // Load domains from domains.txt into a dictionary for O(1) lookup
            if (File.Exists(domainsFilePath))
            {
                var lines = await File.ReadAllLinesAsync(domainsFilePath);
                foreach (var line in lines)
                {
                    var domain = line.Trim();
                    if (!string.IsNullOrEmpty(domain) && !domain.StartsWith("#"))
                    {
                        // Clean domain formatting for PAC
                        string cleanDomain = "";
                        if (domain.StartsWith("domain:"))
                        {
                            cleanDomain = domain.Substring(7).ToLowerInvariant();
                        }
                        else if (domain.StartsWith("full:"))
                        {
                            cleanDomain = domain.Substring(5).ToLowerInvariant();
                        }
                        else if (!domain.Contains("keyword:"))
                        {
                            cleanDomain = (domain.StartsWith(".") ? domain.Substring(1) : domain).ToLowerInvariant();
                        }
                        
                        if (!string.IsNullOrEmpty(cleanDomain))
                        {
                            proxyDomains[cleanDomain] = true;
                        }
                    }
                }
                Logging.SaveLog($"Loaded {proxyDomains.Count} domains for PAC file");
            }

            // Convert dictionary keys to JavaScript object format for fast lookups
            var domainEntries = string.Join(",\n    ", proxyDomains.Keys.Select(d => $"\"{d}\": 1"));

            // Generate optimized PAC JavaScript content - compatible with older browsers
            var pacContent = $@"
function FindProxyForURL(url, host) {{
    // Normalize to lowercase for case-insensitive matching
    host = host.toLowerCase();
    
    // Proxy configuration
    var PROXY = 'PROXY {proxyHost}:{proxyPort}';
    
    // Fast lookup map of domains (O(1) instead of O(n))
    var proxyMap = {{
        {domainEntries}
    }};
    
    // Check exact match first
    if (proxyMap[host]) return PROXY;
    
    // Check parent domains (handles subdomains like sub.example.com -> example.com)
    var parts = host.split('.');
    for (var i = 1; i < parts.length; i++) {{
        var parentDomain = parts.slice(i).join('.');
        if (proxyMap[parentDomain]) return PROXY;
    }}
    
    // Default: direct connection
    return 'DIRECT';
}}";

            Logging.SaveLog($"Generated optimized PAC content for {proxyDomains.Count} domains");
            return pacContent;
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"Error creating PAC content: {ex.Message}");
            // Return basic PAC that sends everything direct as fallback
            return @"
function FindProxyForURL(url, host) {
    return 'DIRECT';
}";
        }
    }
}
