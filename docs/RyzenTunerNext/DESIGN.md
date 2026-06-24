# RyzenTunerNext 技术方案

## 1. 概述

RyzenTunerNext 是 RyzenAdj 的 Windows GUI 前端，用于调整 AMD Ryzen 移动平台的功耗/温度参数。

- **技术栈**：WinUI 3 + C# + SQLite + Dapper + CommunityToolkit.Mvvm + P/Invoke
- **架构**：Windows Service (后台) + WinUI 3 GUI (前端)，x64 目标

---

## 2. 系统架构

### 2.1 双进程模型

```
┌─────────────────────────────────────────────────────┐
│                  Windows Service                     │
│               (SYSTEM 权限, 开机自启)                 │
│                                                     │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────┐ │
│  │ RyzenAdj    │  │ ModeScheduler│  │ SystemEvent│ │
│  │ Wrapper     │  │ (自动模式)   │  │ Monitor    │ │
│  │ (P/Invoke)  │  │              │  │ (电源/唤醒)│ │
│  └──────┬──────┘  └──────┬───────┘  └─────┬──────┘ │
│         └────────┬───────┘                 │        │
│                  ▼                         │        │
│          ParameterApplier ◄────────────────┘        │
│                  │                                   │
│                  ▼                                   │
│         Named Pipe Server                            │
└─────────────────┬───────────────────────────────────┘
                  │  Named Pipe (JSON 消息)
┌─────────────────┴───────────────────────────────────┐
│                   WinUI 3 GUI                        │
│              (普通用户权限, 可随时启停)                │
│                                                     │
│  ┌──────────┐  ┌───────────┐  ┌──────────────────┐ │
│  │ 首页     │  │ 设置页    │  │ 运行日志         │ │
│  │ 状态展示 │  │ 参数配置  │  │ 能效分析         │ │
│  │ 模式切换 │  │ 快捷键    │  │ 关于             │ │
│  └──────────┘  └───────────┘  └──────────────────┘ │
│         │              │                             │
│         └──────┬───────┘                             │
│                ▼                                     │
│         Named Pipe Client                            │
│         SQLite (读写)                                │
└─────────────────────────────────────────────────────┘

共享存储: SQLite (WAL 模式)
```

### 2.2 为什么用双进程

1. **Service 以 SYSTEM 权限运行**，比管理员权限更高，确保 WinRing0 驱动加载稳定
2. **GUI 崩溃不影响参数下发**，Service 持续维持功耗设置
3. **开机自启**，用户登录前即开始工作
4. **GUI 可随时关闭**，不影响后台运行

---

## 3. 解决方案结构

```
RyzenTunerNext/
├── src/
│   ├── RyzenTunerNext.Core/              # 共享核心库 (类库)
│   │   ├── Models/                       # 数据模型
│   │   ├── Services/                     # RyzenAdj 封装
│   │   ├── Data/                         # SQLite + Dapper
│   │   └── Messaging/                    # Named Pipe 协议
│   │
│   ├── RyzenTunerNext.Service/           # Windows Service (控制台应用)
│   │   ├── Program.cs
│   │   ├── Worker.cs                     # BackgroundService
│   │   ├── ModeScheduler.cs              # 自动模式调度
│   │   ├── ParameterApplier.cs           # 参数下发 + 验证
│   │   └── SystemEventMonitor.cs         # 电源/唤醒事件
│   │
│   └── RyzenTunerNext.App/               # WinUI 3 GUI (Unpackaged)
│       ├── App.xaml / App.xaml.cs
│       ├── ViewModels/
│       ├── Views/
│       ├── Controls/                     # 自定义控件
│       └── Helpers/
│
├── lib/                                  # RyzenAdj 运行时依赖
│   ├── libryzenadj.dll
│   ├── WinRing0x64.dll
│   └── WinRing0x64.sys
│
└── docs/
```

---

## 4. 数据库设计 (SQLite + Dapper)

### 4.1 表结构

```sql
-- 应用配置 (键值对)
CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

-- 运行日志
CREATE TABLE logs (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    timestamp  TEXT NOT NULL,           -- ISO 8601
    level      TEXT NOT NULL,           -- Info / Warning / Error
    source     TEXT NOT NULL,           -- Service / GUI / AutoMode
    message    TEXT NOT NULL,
    detail     TEXT
);

-- 能效分析结果
CREATE TABLE profiler_results (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    created_at    TEXT NOT NULL,
    test_type     TEXT NOT NULL,         -- SingleThread / MultiThread
    score         REAL NOT NULL,
    fast_limit    INTEGER NOT NULL,      -- mW
    slow_limit    INTEGER NOT NULL,      -- mW
    tctl_temp     INTEGER NOT NULL,      -- ℃
    avg_frequency REAL,                  -- MHz
    avg_power     REAL,                  -- mW
    max_temp      REAL,                  -- ℃
    efficiency    REAL                   -- score/W
);

CREATE INDEX idx_logs_timestamp ON logs(timestamp DESC);
CREATE INDEX idx_logs_level ON logs(level);
```

### 4.2 配置项 (settings 表)

| key | 默认值 | 说明 |
|-----|--------|------|
| `energy_mode` | `Auto` | Auto / PowerSaving / Performance |
| `fast_limit_performance` | `45000` | 性能模式 Fast Limit (mW) |
| `slow_limit_performance` | `45000` | 性能模式 Slow Limit (mW) |
| `fast_limit_powersaving` | `25000` | 省电模式 Fast Limit (mW) |
| `slow_limit_powersaving` | `15000` | 省电模式 Slow Limit (mW) |
| `tctl_temp` | `90` | Tctl 温度限制 (℃) |
| `apply_interval` | `4000` | 参数循环写入间隔 (ms) |
| `log_retention_days` | `30` | 日志保留天数 |
| `auto_idle_timeout` | `300000` | 自动模式空闲超时 (ms, 5分钟) |
| `anti_cheat_warning_shown` | `false` | 是否已显示反作弊警告 |

### 4.3 Dapper 数据访问

使用轻量 Repository 模式，连接字符串通过 `Microsoft.Data.Sqlite` 提供。

```csharp
public class SettingsRepository
{
    private readonly IDbConnection _db;

    public async Task<string> GetAsync(string key)
    {
        return await _db.QuerySingleOrDefaultAsync<string>(
            "SELECT value FROM settings WHERE key = @Key", new { Key = key });
    }

    public async Task SetAsync(string key, string value)
    {
        await _db.ExecuteAsync(
            "INSERT OR REPLACE INTO settings (key, value) VALUES (@Key, @Value)",
            new { Key = key, Value = value });
    }
}
```

---

## 5. RyzenAdj P/Invoke 封装

### 5.1 原生声明 (RyzenAdjNative.cs)

```csharp
internal static class RyzenAdjNative
{
    private const string DllName = "libryzenadj.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr init_ryzenadj();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void cleanup_ryzenadj(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int get_cpu_family(IntPtr ry);

    // 参数设置 (单位: mW / ℃)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_fast_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_slow_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_stapm_limit(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_tctl_temp(IntPtr ry, uint value);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_power_saving(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int set_max_performance(IntPtr ry);

    // PM Table 操作
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int init_table(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int refresh_table(IntPtr ry);

    // PM Table 读取 (返回 float, 单位: mW / ℃ / MHz)
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_stapm_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_stapm_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_fast_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_fast_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_slow_limit(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_slow_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_tctl_temp(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_tctl_temp_value(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_socket_power(IntPtr ry);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_clk(IntPtr ry, uint core);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern float get_core_temp(IntPtr ry, uint core);
}
```

### 5.2 线程安全封装 (RyzenAdjWrapper.cs)

`ryzen_access` 句柄不是线程安全的。Service 内部通过锁保护所有操作。

```csharp
public sealed class RyzenAdjWrapper : IDisposable
{
    private readonly object _lock = new();
    private IntPtr _handle;
    private bool _tableInitialized;
    private bool _disposed;

    public bool IsInitialized => _handle != IntPtr.Zero;

    public bool Initialize()
    {
        lock (_lock)
        {
            _handle = RyzenAdjNative.init_ryzenadj();
            if (_handle == IntPtr.Zero) return false;

            int tableResult = RyzenAdjNative.init_table(_handle);
            _tableInitialized = tableResult == 0;
            return true;
        }
    }

    /// <summary>
    /// 应用功耗模式并验证实际值。
    /// 返回 (set 是否成功, PM Table 读回的实际值)。
    /// </summary>
    public ApplyResult ApplyProfile(PowerProfile profile)
    {
        lock (_lock)
        {
            if (_disposed || _handle == IntPtr.Zero)
                return ApplyResult.Failed("RyzenAdj 未初始化");

            // 1. 设置 power mode flag
            if (profile.Mode == EnergyMode.PowerSaving)
                RyzenAdjNative.set_power_saving(_handle);
            else
                RyzenAdjNative.set_max_performance(_handle);

            // 2. 设置核心参数
            int err;
            err = RyzenAdjNative.set_fast_limit(_handle, (uint)profile.FastLimit);
            if (err != 0) return ApplyResult.Failed($"set_fast_limit 失败: {err}");

            err = RyzenAdjNative.set_slow_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_slow_limit 失败: {err}");

            // stapm-limit = slow-limit
            err = RyzenAdjNative.set_stapm_limit(_handle, (uint)profile.SlowLimit);
            if (err != 0) return ApplyResult.Failed($"set_stapm_limit 失败: {err}");

            err = RyzenAdjNative.set_tctl_temp(_handle, (uint)profile.TctlTemp);
            if (err != 0) return ApplyResult.Failed($"set_tctl_temp 失败: {err}");

            // 3. 验证: 刷新 PM Table 读回实际值
            var actual = ReadActualValues();
            return ApplyResult.Success(actual);
        }
    }

    /// <summary>
    /// 读取 PM Table 中的实际生效值。
    /// SMU 可能返回成功但值被 BIOS cap 截断，必须验证。
    /// </summary>
    public ActualValues ReadActualValues()
    {
        lock (_lock)
        {
            if (!_tableInitialized) return ActualValues.Empty;

            RyzenAdjNative.refresh_table(_handle);

            return new ActualValues
            {
                FastLimit = RyzenAdjNative.get_fast_limit(_handle),
                FastValue = RyzenAdjNative.get_fast_value(_handle),
                SlowLimit = RyzenAdjNative.get_slow_limit(_handle),
                SlowValue = RyzenAdjNative.get_slow_value(_handle),
                StapmLimit = RyzenAdjNative.get_stapm_limit(_handle),
                StapmValue = RyzenAdjNative.get_stapm_value(_handle),
                TctlTemp = RyzenAdjNative.get_tctl_temp(_handle),
                TctlTempValue = RyzenAdjNative.get_tctl_temp_value(_handle),
                SocketPower = RyzenAdjNative.get_socket_power(_handle),
            };
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_disposed && _handle != IntPtr.Zero)
            {
                RyzenAdjNative.cleanup_ryzenadj(_handle);
                _handle = IntPtr.Zero;
            }
            _disposed = true;
        }
    }
}
```

### 5.3 错误码定义

```csharp
public enum RyzenAdjError
{
    Success = 0,
    FamUnsupported = -1,
    SmuTimeout = -2,
    SmuUnsupported = -3,
    SmuRejected = -4,
    MemoryAccess = -5,
}
```

---

## 6. Windows Service

### 6.1 Worker (主循环)

继承 `BackgroundService`，核心流程：

1. 初始化 RyzenAdj (加载 libryzenadj.dll → WinRing0x64.dll → WinRing0x64.sys)
2. 加载 SQLite 配置
3. 启动 Named Pipe Server
4. 进入定时循环：根据当前模式计算参数 → 下发 → 验证 → 推送状态
5. 监听系统事件 (电源切换/唤醒)，收到事件立即触发一次下发

```csharp
public class Worker : BackgroundService
{
    private readonly RyzenAdjWrapper _ryzenAdj;
    private readonly SettingsRepository _settings;
    private readonly LogRepository _logs;
    private readonly PipeServer _pipeServer;
    private readonly SystemEventMonitor _eventMonitor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 初始化 RyzenAdj
        if (!_ryzenAdj.Initialize())
        {
            await _logs.ErrorAsync("Service", "RyzenAdj 初始化失败，30 秒后重试...");
            // 重试逻辑...
        }

        // 启动 Named Pipe Server
        _ = _pipeServer.StartAsync(stoppingToken);

        // 注册系统事件
        _eventMonitor.WakeUp += OnSystemEvent;
        _eventMonitor.PowerSourceChanged += OnSystemEvent;

        // 主循环
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var interval = await _settings.GetApplyIntervalAsync();
                var profile = await GetCurrentProfileAsync();
                var result = _ryzenAdj.ApplyProfile(profile);

                // 验证: 设置值 vs 实际值
                await VerifyAndLogAsync(profile, result);

                // 推送状态到 GUI
                await _pipeServer.BroadcastStatusAsync(result);

                await Task.Delay(interval, stoppingToken);
            }
            catch (Exception ex)
            {
                await _logs.ErrorAsync("Service", $"参数下发异常: {ex.Message}");
            }
        }
    }

    private async void OnSystemEvent(object sender, EventArgs e)
    {
        // 唤醒/电源切换时立即触发一次，不等定时器
        var profile = await GetCurrentProfileAsync();
        var result = _ryzenAdj.ApplyProfile(profile);
        await _pipeServer.BroadcastStatusAsync(result);
    }
}
```

### 6.2 SystemEventMonitor

监听 Windows 系统级事件，唤醒/电源切换时立即触发参数下发。

```csharp
public class SystemEventMonitor
{
    public event EventHandler WakeUp;
    public event EventHandler PowerSourceChanged;

    // 方案: 订阅 SystemEvents.PowerModeChanged
    // + 注册 PowerSettingNotification (GUID_ACDC_POWER_SOURCE, GUID_MONITOR_POWER_ON)
    // 当收到 Resume / PowerSourceChange 事件时触发
}
```

### 6.3 Service 安装/卸载

GUI 中提供按钮，通过 `sc.exe` 或 `System.ServiceProcess` API 实现：

- 安装: `sc.exe create RyzenTunerNext binPath= "path\to\service.exe" start= auto`
- 卸载: `sc.exe delete RyzenTunerNext`
- 启动/停止: `sc.exe start/stop RyzenTunerNext`

GUI 启动时检测 Service 状态，未安装则提示安装。

---

## 7. Named Pipe 通信协议

### 7.1 协议设计

- **管道名**: `RyzenTunerNext_Pipe`
- **方向**: 双向 (Service ↔ GUI)
- **消息格式**: JSON 序列化，长度前缀 (4 字节 uint32 + JSON payload)
- **连接管理**: Service 为 Server，支持 GUI 重连

### 7.2 消息类型

**Service → GUI:**

```jsonc
// 状态推送 (每次参数下发后)
{
    "type": "status_update",
    "mode": "Performance",           // 当前模式
    "set_limits": {                  // 本次设置的值
        "fast_limit": 45000,
        "slow_limit": 45000,
        "tctl_temp": 90
    },
    "actual_limits": {               // PM Table 读回的实际值
        "fast_limit": 30000,         // 可能被 BIOS cap
        "slow_limit": 30000,
        "tctl_temp": 90,
        "socket_power": 25000,
        "cpu_temp": 75
    },
    "timestamp": "2024-01-01T12:00:00Z"
}

// 日志推送
{
    "type": "log",
    "level": "Warning",
    "source": "Service",
    "message": "Fast Limit 被 BIOS cap: 设置 45000mW, 实际 30000mW",
    "timestamp": "2024-01-01T12:00:00Z"
}
```

**GUI → Service:**

```jsonc
// 切换模式
{ "type": "set_mode", "mode": "PowerSaving" }

// 立即应用 (用户手动点击)
{ "type": "apply_now" }

// 更新配置 (设置页修改后)
{ "type": "update_config", "key": "fast_limit_performance", "value": "50000" }
```

---

## 8. WinUI 3 GUI

### 8.1 页面结构

采用 `NavigationView` 侧边栏导航，5 个页面：

| 页面 | 说明 |
|------|------|
| **首页** | 模式切换 + 实时状态 + 生效参数展示 |
| **设置** | 省电/性能模式参数配置 + 全局设置 + 快捷键 |
| **运行日志** | 日志列表 + 筛选 + 搜索 |
| **能效分析** | 基准测试 + 结果对比 |
| **关于** | 版本信息 + 运行状态 |

### 8.2 首页

布局参考 `docs/RyzenTunerNext/UI/v1.html`：

- **能耗模式切换**: 使用 `ComboBox`（自动 / 省电模式 / 性能模式），带 tooltip 说明
- **当前状态卡片**: CPU 频率 / 整机功耗 / CPU 温度，数值 + 单位
- **生效参数卡片**: Fast Limit / Slow Limit / Tctl 温度
  - 当设置值 ≠ 实际值时，用 InfoBar 提示 "Fast Limit 设置 45W，实际生效 30W (可能被 BIOS 锁定)"

使用 WinUI 图标，不使用 emoji：
- 模式切换图标: `&#xE945;` (Brightness)
- CPU 图标: `&#xE950;` (DeveloperTools)
- 温度图标: `&#xEB91;` (Temperature)
- 功耗图标: `&#xE945;` (PowerButton)

### 8.3 设置页

- **参数配置**: 省电模式和性能模式各一组 NumberBox（Fast Limit / Slow Limit）
- **全局配置**: Tctl 温度 NumberBox（80-95℃）、更新频率 NumberBox
- **快捷键**: 三个 TextBox 显示/录制快捷键组合
- **常规**: 主题（亮色/暗色）、语言、日志保留天数

参数说明通过 Tooltip 显示：
- Fast Limit: "峰值功耗上限 (PPT FAST)。必须大于等于 Slow Limit。部分机型 BIOS 锁定了最大值。"
- Slow Limit: "持续功耗上限 (PPT SLOW)。STAPM Limit 会自动等于此值。"
- Tctl 温度: "CPU 温度目标。建议比 Prochot 温度低 5-10℃。过低会触发降频。"

### 8.4 运行日志页

- **筛选栏**: 搜索框 (AutoSuggestBox) + 日志级别下拉 (所有/Info/Warning/Error)
- **日志列表**: `DataGrid` 或 `ListView`，列: 时间 / 级别 / 来源 / 详情
- 使用 WinUI 图标标记日志级别:
  - Info: `&#xE946;` (Info)
  - Warning: `&#xE7BA;` (Warning)
  - Error: `&#xE783;` (Error)

### 8.5 系统托盘

GUI 最小化或空闲时缩到系统托盘，用户离开时减少窗口干扰，回来时可快速恢复。

**托盘图标**：
- 使用 WinUI 3 的 `TrayIcon`（`Microsoft.UI.Xaml` 或 `H.NotifyIcon`）
- 左键双击：恢复主窗口
- 右键菜单：

```
┌─────────────────────────────────┐
│  RyzenTunerNext                 │
│─────────────────────────────────│
│  Service 状态: 运行中            │  (灰色/只读)
│  当前模式: 性能模式              │  (灰色/只读)
│  Fast Limit: 45W → 实际 30W     │  (灰色/只读)
│  CPU 温度: 72℃ | 功耗: 28W     │  (灰色/只读)
│─────────────────────────────────│
│  ○ 自动                         │  (可点击切换)
│  ○ 省电模式                     │  (可点击切换)
│  ○ 性能模式                     │  (可点击切换)
│─────────────────────────────────│
│  打开主窗口                     │  (恢复窗口)
│  退出                           │  (退出程序)
└─────────────────────────────────┘
```

**实时刷新**：右键菜单中的状态信息（Service 状态、模式、功耗参数、实际值、CPU 温度）跟随 Service 的 Named Pipe 推送实时更新，频率与参数下发间隔一致（默认 4 秒）。每次收到 `StatusUpdate` 消息时，更新菜单中的只读项文本和 Tooltip。

**最小化到托盘的触发条件**：
- 用户点击窗口关闭按钮 → 最小化到托盘（不退出）
- 自动模式判定用户离开时（仅电池供电 + 无输入 ≥ 5 分钟）→ GUI 同时自动最小化到托盘
  - 与自动模式的空闲检测共用同一个 `GetLastInputInfo` 计时，无需额外检测
  - 仅在自动模式 + 电池供电时生效，AC 电源或手动模式下不会自动最小化

**恢复窗口的条件**：
- 用户有键盘/鼠标输入 → 自动恢复窗口（与自动模式切回性能模式同步）

**托盘图标 Tooltip**：
鼠标悬停时显示当前状态摘要：`RyzenTunerNext | 性能模式 | 45W | 72℃`

### 8.6 能效分析页

自写 C# 基准测试（零外部依赖），用于在同一台机器上对比不同功耗设置下的性能差异。

**基准测试算法**：多线程素数计数（Eratosthenes 筛法变体），计算密集型，纯 CPU 运算，无 I/O 依赖。

```csharp
/// <summary>
/// 多核跑分: 统计 [2, upperBound] 范围内的素数个数。
/// Parallel.For 占满所有逻辑核心。
/// 返回素数个数，耗时由 Stopwatch 计量。
/// </summary>
public static (long primeCount, double elapsedMs) RunMultiCore(long upperBound = 5_000_000)
{
    var sw = Stopwatch.StartNew();
    long count = 0;

    // 按区间分块，每个线程独立筛选，避免锁竞争
    int coreCount = Environment.ProcessorCount;
    long chunkSize = upperBound / coreCount;

    Parallel.For(0, coreCount, i =>
    {
        long start = i * chunkSize + 2;
        long end = (i == coreCount - 1) ? upperBound : (i + 1) * chunkSize + 1;
        long localCount = CountPrimesInRange(start, end);
        Interlocked.Add(ref count, localCount);
    });

    sw.Stop();
    return (count, sw.Elapsed.TotalMilliseconds);
}

public static (long primeCount, double elapsedMs) RunSingleCore(long upperBound = 1_000_000)
{
    var sw = Stopwatch.StartNew();
    long count = CountPrimesInRange(2, upperBound);
    sw.Stop();
    return (count, sw.Elapsed.TotalMilliseconds);
}
```

**测试流程**：
1. 用户选择测试类型（单核/多核）
2. 依次测试不同功耗限制（如 15W/25W/35W/45W）下的跑分
3. 每档功耗设置后等待 2 秒稳定，运行基准，记录：跑分耗时、平均 CPU 频率 (get_core_clk)、实际功耗 (get_socket_power)、最高温度 (get_tctl_temp_value)
4. 计算能效比 = 跑分成绩 / 实际功耗W
5. 结果存入 SQLite `profiler_results` 表
6. 提供建议：省电模式推荐值、性能甜点区间

**结果展示列**：

| 设定功耗 | 跑分成绩 | 平均 CPU 频率 | 实际功耗 | CPU 最高温度 | 能效比 |
|---------|---------|-------------|---------|------------|-------|
| 15 W | 4,200 | 2800 MHz | 15.2 W | 55 ℃ | 276 Pts/W |
| 30 W | 8,500 | 3600 MHz | 30.5 W | 72 ℃ | 278 Pts/W |
| 45 W | 9,100 | 4200 MHz | 45.1 W | 88 ℃ | 201 Pts/W |

CPU 频率在测试过程中通过 PM Table 的 `get_core_clk()` 采样，取所有核心的平均值，测试结束后计算总体平均。

**为什么不用外部工具**：
- CPU-Z Benchmark 算法私有且不可嵌入
- Cinebench / Geekbench 需要额外打包大型 exe
- WinSAT 已在 Windows 11 移除
- 自写方案零依赖、完全可控，且场景是同一台机器的相对对比，不需要跨机器公信力

### 8.7 控件选型

| 场景 | 控件 | 说明 |
|------|------|------|
| 数值输入 (功耗/温度) | `NumberBox` | 带 +/- 微调按钮，不用 Slider |
| 模式切换 | `ComboBox` | 下拉选择 |
| 状态卡片 | 自定义 `UserControl` | 参考 v1.html 的 stat-box 设计 |
| 日志表格 | `DataGrid` / `ListView` | 虚拟化，支持大量日志 |
| Tooltip | `ToolTipService` | 参数说明 |
| 快捷键录制 | 自定义 `HotKeyBox` | 捕获键盘组合 |

### 8.8 MVVM 结构

使用 `CommunityToolkit.Mvvm`：

```csharp
public partial class HomeViewModel : ObservableObject
{
    [ObservableProperty] private string _currentMode = "Auto";
    [ObservableProperty] private double _cpuFrequency;
    [ObservableProperty] private double _socketPower;
    [ObservableProperty] private double _cpuTemp;
    [ObservableProperty] private double _fastLimitActual;
    [ObservableProperty] private double _slowLimitActual;
    [ObservableProperty] private double _tctlTempActual;
    [ObservableProperty] private string _capWarning;

    [RelayCommand]
    private async Task SwitchModeAsync(string mode)
    {
        // 通过 Named Pipe 通知 Service
        await _pipeClient.SendAsync(new SetModeMessage(mode));
    }
}
```

---

## 9. 自动模式调度

### 9.1 切换逻辑

自动模式基于两个维度决定能耗模式：**电源状态** + **用户活动**。

```
┌─────────────────────────────────────────────────────────────┐
│                       自动模式                               │
│                                                             │
│  电源状态判断 (优先级高，立即响应):                           │
│    AC 电源接入  → 性能模式                                   │
│    电池供电     → 进入空闲检测                               │
│                                                             │
│  空闲检测 (仅电池供电时生效):                                │
│    用户无输入 ≥ 5 分钟 → 省电模式                            │
│    用户有输入         → 性能模式                             │
│                                                             │
│  防抖:                                                      │
│    任意切换后冷却 10 分钟                                    │
│    电池 → 省电: 满足条件后延迟 1 分钟再切换                  │
│    空闲 → 性能: 立即切换 (用户操作需要快速响应)              │
└─────────────────────────────────────────────────────────────┘
```

**设计思路**：
- 笔记本接 AC 电源时通常需要性能，用电池时需要省电
- 电池供电 + 用户无操作 = 可以安全降低功耗
- 去掉 CPU 负载检测，避免引入额外依赖（PerformanceCounter/WMI），且电源状态 + 用户输入已足够判断

### 9.2 电源状态检测

使用 Win32 API `GetSystemPowerStatus`：

```csharp
[DllImport("kernel32.dll")]
static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

[StructLayout(LayoutKind.Sequential)]
struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;        // 0 = 电池, 1 = AC, 255 = 未知
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte Reserved1;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}

public static bool IsOnAcPower()
{
    GetSystemPowerStatus(out var status);
    return status.ACLineStatus == 1;
}
```

同时监听电源切换事件（参见 6.2 SystemEventMonitor），AC/电池切换时立即触发模式切换。

### 9.3 用户输入检测

使用 Win32 API `GetLastInputInfo`，轻量无侵入：

```csharp
[DllImport("user32.dll")]
static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

public static TimeSpan GetIdleTime()
{
    var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
    GetLastInputInfo(ref info);
    return TimeSpan.FromMilliseconds(Environment.TickCount - info.dwTime);
}
```

---

## 10. 管理员权限与反作弊

### 10.1 管理员权限

WinUI 3 Unpackaged 模式 + `app.manifest` 声明 `requireAdministrator`：

```xml
<requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
```

非管理员启动时: 弹出 ContentDialog 提示，提供"以管理员重新启动"按钮（`ShellExecute` + `runas`）。

### 10.2 Self-Contained 部署

```xml
<PropertyGroup>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
</PropertyGroup>
```

### 10.3 反作弊警告

首次启动时弹出一次性警告 ContentDialog：
- 说明 WinRing0 驱动可能被反作弊/杀毒软件拦截
- 不包含 `inpoutx64.dll`（已被反作弊广泛标记）
- 用户确认后存入 `settings.anti_cheat_warning_shown = true`

---

## 11. 设计规范

参考 `docs/RyzenTunerNext/UI/DESIGN.md` (Apple 设计语言分析)，但实际 UI 遵循 WinUI 3 Fluent Design：

- **配色**: WinUI 默认主题色 (`#005FB8`)，亮色/暗色主题
- **字体**: Segoe UI Variable
- **图标**: Segoe Fluent Icons 字体图标，不使用 emoji
- **单位统一**: 温度 `℃`，功耗 `W`，频率 `MHz`
- **信息密度**: 核心信息直接展示，补充信息放 Tooltip
- **间距**: 8px 基础单位

---

## 12. 开放问题

| # | 问题 | 当前方案 |
|---|------|---------|
| 1 | Service 安装/卸载的权限检测 | GUI 以管理员运行，直接调用 `sc.exe` |
| 2 | Service 崩溃后的自动恢复 | 配置 `sc.exe failure reset= 86400 actions= restart/5000` |
| 3 | 多显示器下窗口位置 | 记住上次窗口位置到 SQLite |
| 4 | 日志自动清理 | 后台定时任务，按 `log_retention_days` 配置清理 |
