using System.Net.Sockets;
using System.Text;
using ServiceLib.Common;

namespace ServiceLib.Manager;

public class PacManager
{
    private static readonly Lazy<PacManager> _instance = new(() => new PacManager());
    public static PacManager Instance => _instance.Value;

    private string _configPath;
    private int _httpPort;
    private int _pacPort;
    private TcpListener? _tcpListener;
    private byte[] _writeContent;
    private bool _isRunning;
    private bool _needRestart = true;

    public async Task StartAsync(string configPath, int httpPort, int pacPort)
    {
        _needRestart = configPath != _configPath || httpPort != _httpPort || pacPort != _pacPort || !_isRunning;

        _configPath = configPath;
        _httpPort = httpPort;
        _pacPort = pacPort;

        await InitText();

        if (_needRestart)
        {
            Stop();
            RunListener();
        }
    }

    private async Task InitText()
    {
        var path = Path.Combine(_configPath, "pac.txt");

        // Delete the old pac file
        if (File.Exists(path) && Utils.GetFileHash(path).Equals("b590c07280f058ef05d5394aa2f927fe"))
        {
            File.Delete(path);
        }

        // Build PAC text: prefer dynamic generation from domains.txt to proxy only those domains
        string pacText;
        try
        {
            // Look for domains.txt in multiple locations: working directory, app directory, config path
            var domainsFile = "";
            var candidatePaths = new[]
            {
                "domains.txt", // Current working directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "domains.txt"), // App directory
                Path.Combine(_configPath, "..", "domains.txt"), // Parent of config directory
                Path.Combine(Environment.CurrentDirectory, "domains.txt") // Current directory
            };
            
            foreach (var candidate in candidatePaths)
            {
                if (File.Exists(candidate))
                {
                    domainsFile = candidate;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(domainsFile) && File.Exists(domainsFile))
            {
                var lines = (await File.ReadAllLinesAsync(domainsFile))
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith("#"))
                    .ToList();

                var sbPac = new StringBuilder();
                sbPac.AppendLine("function dnsDomainIs(host, domain) { return host === domain || host.endsWith('.' + domain); }");
                sbPac.AppendLine("function shExpMatch(url, pattern) { try { return new RegExp(pattern.replace(/[.]/g,'\\\\.').replace('*','.*')).test(url);} catch(e){return false;} }");
                sbPac.AppendLine("function FindProxyForURL(url, host) {");
                foreach (var d in lines)
                {
                    if (d.StartsWith("keyword:"))
                    {
                        var kw = d.Substring("keyword:".Length);
                        if (!string.IsNullOrWhiteSpace(kw))
                        {
                            sbPac.AppendLine($"  if (shExpMatch(host, '.*{kw}.*')) return 'PROXY 127.0.0.1:{_httpPort}';");
                        }
                        continue;
                    }
                    var domain = d.StartsWith(".") ? d.Substring(1) : d;
                    if (domain.StartsWith("full:"))
                    {
                        var full = domain.Substring("full:".Length);
                        sbPac.AppendLine($"  if (host === '{full}') return 'PROXY 127.0.0.1:{_httpPort}';");
                    }
                    else if (domain.StartsWith("domain:"))
                    {
                        var dom = domain.Substring("domain:".Length);
                        sbPac.AppendLine($"  if (dnsDomainIs(host, '{dom}')) return 'PROXY 127.0.0.1:{_httpPort}';");
                    }
                    else
                    {
                        sbPac.AppendLine($"  if (dnsDomainIs(host, '{domain}')) return 'PROXY 127.0.0.1:{_httpPort}';");
                    }
                }
                // Default DIRECT for everything else
                sbPac.AppendLine("  return 'DIRECT';");
                sbPac.AppendLine("}");
                pacText = sbPac.ToString();
            }
            else
            {
                if (!File.Exists(path))
                {
                    var pac = EmbedUtils.GetEmbedText(Global.PacFileName);
                    await File.AppendAllTextAsync(path, pac);
                }
                pacText = (await File.ReadAllTextAsync(path)).Replace("__PROXY__", $"PROXY 127.0.0.1:{_httpPort};DIRECT;");
            }
        }
        catch
        {
            if (!File.Exists(path))
            {
                var pac = EmbedUtils.GetEmbedText(Global.PacFileName);
                await File.AppendAllTextAsync(path, pac);
            }
            pacText = (await File.ReadAllTextAsync(path)).Replace("__PROXY__", $"PROXY 127.0.0.1:{_httpPort};DIRECT;");
        }

        // Remove DIRECT fallback from proxy rules - listed domains should only use proxy
        var beforeCount = pacText.Split($"'PROXY 127.0.0.1:{_httpPort};DIRECT'").Length - 1;
        pacText = pacText.Replace($"'PROXY 127.0.0.1:{_httpPort};DIRECT'", $"'PROXY 127.0.0.1:{_httpPort}'");
        var afterCount = pacText.Split($"'PROXY 127.0.0.1:{_httpPort}'").Length - 1;
        Logging.SaveLog($"PAC DIRECT removal: Before={beforeCount}, After={afterCount}, Port={_httpPort}");
        
        var sb = new StringBuilder();
        sb.AppendLine("HTTP/1.0 200 OK");
        sb.AppendLine("Content-type:application/x-ns-proxy-autoconfig");
        sb.AppendLine("Connection:close");
        sb.AppendLine("Content-Length:" + Encoding.UTF8.GetByteCount(pacText));
        sb.AppendLine();
        sb.Append(pacText);
        _writeContent = Encoding.UTF8.GetBytes(sb.ToString());
    }

    private void RunListener()
    {
        _tcpListener = TcpListener.Create(_pacPort);
        _isRunning = true;
        _tcpListener.Start();
        Task.Factory.StartNew(async () =>
        {
            while (_isRunning)
            {
                try
                {
                    if (!_tcpListener.Pending())
                    {
                        await Task.Delay(10);
                        continue;
                    }

                    var client = await _tcpListener.AcceptTcpClientAsync();
                    await Task.Run(() => WriteContent(client));
                }
                catch
                {
                    // ignored
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    private void WriteContent(TcpClient client)
    {
        var stream = client.GetStream();
        stream.Write(_writeContent, 0, _writeContent.Length);
        stream.Flush();
    }

    public void Stop()
    {
        if (_tcpListener == null)
        {
            return;
        }
        try
        {
            _isRunning = false;
            _tcpListener.Stop();
            _tcpListener = null;
        }
        catch
        {
            // ignored
        }
    }
}
