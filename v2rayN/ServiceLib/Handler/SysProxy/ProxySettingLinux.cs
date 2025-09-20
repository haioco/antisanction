using ServiceLib.Manager;

namespace ServiceLib.Handler.SysProxy;

public static class ProxySettingLinux
{
    private static readonly string _tag = "ProxySettingLinux";
    private static readonly string _proxySetFileName = $"{Global.ProxySetLinuxShellFileName.Replace(Global.NamespaceSample, "")}.sh";

    public static async Task SetProxy(string host, int port, string exceptions)
    {
        Logging.SaveLog($"SetProxy called - Host: {host}, Port: {port}, Exceptions: '{exceptions}'");
        NoticeManager.Instance.SendMessage($"Executing manual proxy setup: {host}:{port}");
        List<string> args = ["manual", host, port.ToString(), exceptions];
        await ExecCmd(args, "SetProxy");
    }

    public static async Task UnsetProxy()
    {
        Logging.SaveLog("UnsetProxy called");
        NoticeManager.Instance.SendMessage("Executing proxy removal");
        List<string> args = ["none"];
        await ExecCmd(args, "UnsetProxy");
    }

    public static async Task SetPacProxy(string pacUrl)
    {
        Logging.SaveLog($"SetPacProxy called - PAC URL: {pacUrl}");
        NoticeManager.Instance.SendMessage($"Executing PAC proxy setup: {pacUrl}");
        List<string> args = ["auto", pacUrl];
        await ExecCmd(args, "SetPacProxy");
    }

    private static async Task ExecCmd(List<string> args, string operationName = "ExecCmd")
    {
        try
        {
            // Ensure the script exists on disk with executable permissions
            Logging.SaveLog($"{operationName} - Creating shell file: {_proxySetFileName}");
            var filePath = await FileManager.CreateLinuxShellFile(
                _proxySetFileName,
                EmbedUtils.GetEmbedText(Global.ProxySetLinuxShellFileName),
                overwrite: true
            );
            Logging.SaveLog($"{operationName} - Shell file path: {filePath}");

            // Execute via CliWrap helper (captures stdout/stderr); returns null on failure/exception
            Logging.SaveLog($"{operationName}: Executing '{filePath}' with args: {string.Join(" ", args)}");
            var output = await Utils.GetCliWrapOutput(filePath, args);

            if (output is null)
            {
                var msg = $"{operationName} failed: no output (non-zero exit code or exception)";
                Logging.SaveLog(msg);
                NoticeManager.Instance.SendMessage(msg);
                throw new Exception(msg);
            }

            // Heuristic: if script printed to stderr, CliWrap still gives output only when success.
            NoticeManager.Instance.SendMessage($"{operationName} completed successfully");
            Logging.SaveLog($"{operationName}: {output}");
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ERROR in {operationName}: {ex.Message}");
            Logging.SaveLog($"Exception details: {ex}");
            throw;
        }
    }
}
