using System.Diagnostics;
using System.ServiceProcess;

namespace RyzenTunerNext.App.Helpers;

/// <summary>
/// Windows Service 管理：安装/卸载/启动/停止/查询状态。
/// 通过 sc.exe 实现。
/// </summary>
internal static class ServiceManager
{
    private const string ServiceName = "RyzenTunerNext";

    /// <summary>
    /// 获取 Service 当前状态
    /// </summary>
    public static ServiceState GetServiceState()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            // 检查服务是否存在
            _ = sc.Status;
            return new ServiceState
            {
                IsInstalled = true,
                IsRunning = sc.Status == ServiceControllerStatus.Running,
                StatusText = sc.Status.ToString()
            };
        }
        catch (InvalidOperationException)
        {
            // 服务不存在
            return new ServiceState { IsInstalled = false, IsRunning = false, StatusText = "未安装" };
        }
        catch
        {
            return new ServiceState { IsInstalled = false, IsRunning = false, StatusText = "检测失败" };
        }
    }

    /// <summary>
    /// 安装 Service
    /// </summary>
    public static async Task<(bool Success, string Message)> InstallAsync()
    {
        var serviceExePath = GetServiceExePath();
        if (string.IsNullOrEmpty(serviceExePath))
        {
            return (false, "找不到 Service 可执行文件。请确认 RyzenTunerNext.Service.exe 存在。");
        }

        // 将 App 的数据库路径传递给 Service，确保两者共享同一个数据库
        var dbPath = Path.Combine(AppContext.BaseDirectory, "RyzenTunerNext.db");
        var binPath = $"\"{serviceExePath}\" --db-path \"{dbPath}\"";
        var result = await RunScAsync($"create {ServiceName} binPath= {binPath} start= auto DisplayName= \"RyzenTunerNext Service\"");
        if (result.Success)
        {
            // 设置失败恢复策略
            await RunScAsync($"failure {ServiceName} reset= 86400 actions= restart/5000");
            return (true, "Service 安装成功");
        }
        return (false, $"安装失败: {result.Output}");
    }

    /// <summary>
    /// 卸载 Service
    /// </summary>
    public static async Task<(bool Success, string Message)> UninstallAsync()
    {
        // 先停止
        await StopAsync();
        await Task.Delay(1000);

        var result = await RunScAsync($"delete {ServiceName}");
        if (result.Success)
        {
            return (true, "Service 已卸载");
        }
        return (false, $"卸载失败: {result.Output}");
    }

    /// <summary>
    /// 启动 Service
    /// </summary>
    public static async Task<(bool Success, string Message)> StartAsync()
    {
        var result = await RunScAsync($"start {ServiceName}");
        if (result.Success)
        {
            return (true, "Service 已启动");
        }
        return (false, $"启动失败: {result.Output}");
    }

    /// <summary>
    /// 停止 Service
    /// </summary>
    public static async Task<(bool Success, string Message)> StopAsync()
    {
        var result = await RunScAsync($"stop {ServiceName}");
        if (result.Success)
        {
            return (true, "Service 已停止");
        }
        return (false, $"停止失败: {result.Output}");
    }

    internal static string? GetServiceExePath()
    {
        var appDir = AppContext.BaseDirectory;

        // 1. Service/ 子目录（单一 zip 解压结构）
        var serviceExe = Path.Combine(appDir, "Service", "RyzenTunerNext.Service.exe");
        if (File.Exists(serviceExe)) return serviceExe;

        // 2. 同目录（兼容手动放置场景）
        serviceExe = Path.Combine(appDir, "RyzenTunerNext.Service.exe");
        if (File.Exists(serviceExe)) return serviceExe;

        // 3. 上级目录的 sibling（旧的两 zip 解压结构，向后兼容）
        var parentDir = Path.GetDirectoryName(appDir);
        if (parentDir != null)
        {
            serviceExe = Path.Combine(parentDir, "RyzenTunerNext.Service", "RyzenTunerNext.Service.exe");
            if (File.Exists(serviceExe)) return serviceExe;
        }

        return null;
    }

    /// <summary>
    /// 查询已注册的 Service 可执行文件路径（从注册表读取）。
    /// 用于判断 Service 是否需要重新安装（路径变更时）。
    /// </summary>
    public static string? GetInstalledServiceExePath()
    {
        var binPath = GetInstalledBinPath();
        if (string.IsNullOrEmpty(binPath)) return null;

        // 提取 exe 路径（可能被引号包围，后面可能有参数）
        if (binPath.StartsWith('"'))
        {
            var endQuote = binPath.IndexOf('"', 1);
            return endQuote > 0 ? binPath[1..endQuote] : binPath.Trim('"');
        }

        // 无引号：取第一个空格之前的部分
        var spaceIndex = binPath.IndexOf(' ');
        return spaceIndex > 0 ? binPath[..spaceIndex] : binPath;
    }

    /// <summary>
    /// 查询已注册的完整 binPath（包含参数），用于判断是否需要重新安装。
    /// </summary>
    public static string? GetInstalledBinPath()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{ServiceName}");
            if (key == null) return null;

            return key.GetValue("ImagePath") as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查已安装的 Service 是否需要重新安装（路径变更或缺少 --db-path 参数）。
    /// </summary>
    public static bool NeedsReinstall(out string reason)
    {
        reason = string.Empty;
        var expectedExe = GetServiceExePath();
        if (string.IsNullOrEmpty(expectedExe))
        {
            reason = "找不到 Service 可执行文件";
            return false;
        }

        var installedBinPath = GetInstalledBinPath();
        if (string.IsNullOrEmpty(installedBinPath))
        {
            reason = "Service 未安装";
            return false;
        }

        var installedExe = GetInstalledServiceExePath();

        // 检查 exe 路径是否变更
        if (!string.IsNullOrEmpty(installedExe) &&
            !string.Equals(installedExe, expectedExe, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"路径变更: {installedExe} -> {expectedExe}";
            return true;
        }

        // 检查是否缺少 --db-path 参数（旧版本安装）
        if (!installedBinPath.Contains("--db-path", StringComparison.OrdinalIgnoreCase))
        {
            reason = "缺少 --db-path 参数（旧版本安装）";
            return true;
        }

        return false;
    }

    private static async Task<(bool Success, string Output)> RunScAsync(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (false, "无法启动 sc.exe");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var success = process.ExitCode == 0;
            var message = success ? output.Trim() : (error.Trim() + " " + output.Trim()).Trim();
            return (success, message);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}

internal class ServiceState
{
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public string StatusText { get; set; } = string.Empty;
}
