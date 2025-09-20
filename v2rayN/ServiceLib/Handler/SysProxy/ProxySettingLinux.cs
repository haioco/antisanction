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
            Logging.SaveLog($"{operationName}: Executing shell script with args: {string.Join(" ", args)}");
            
            var result = await ExecuteShellScript(_proxySetFileName, args);
            
            Logging.SaveLog($"{operationName}: Shell script result - Success: {result.success}, Output: '{result.output}'");
            
            if (!result.success)
            {
                Logging.SaveLog($"ERROR in {operationName}: Shell script failed - {result.output}");
                NoticeManager.Instance.SendMessage($"{operationName} failed: {result.output}");
                throw new Exception($"Shell script execution failed: {result.output}");
            }
            else
            {
                NoticeManager.Instance.SendMessage($"{operationName} completed successfully: {result.output}");
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog($"ERROR in {operationName}: {ex.Message}");
            Logging.SaveLog($"Exception details: {ex}");
            throw;
        }
    }

    private static async Task<(bool success, string output)> ExecuteShellScript(string scriptName, List<string> args)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "bash";
            process.StartInfo.Arguments = $"{scriptName} {string.Join(" ", args)}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var fullOutput = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}";
            return (process.ExitCode == 0, fullOutput.Trim());
        }
        catch (Exception ex)
        {
            return (false, $"Exception executing script: {ex.Message}");
        }
    }
}
