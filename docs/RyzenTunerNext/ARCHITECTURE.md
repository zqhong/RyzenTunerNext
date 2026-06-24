# RyzenTunerNext 架构设计

## 1. 系统架构

### 1.1 双进程架构

RyzenTunerNext 采用 Windows Service + WinUI 3 GUI 的双进程架构。

**为什么不用单进程？**

RyzenAdj 需要管理员权限加载内核驱动。将参数下发逻辑放在 Windows Service 中：
- 以 SYSTEM 权限运行，比管理员更高，确保驱动加载可靠
- 支持开机自启，用户登录前就开始工作
- GUI 崩溃不影响后台参数维持（RyzenAdj 设置会被系统电源策略覆盖，必须持续写入）

### 1.2 通信方式

```
┌─────────────────┐         Named Pipe          ┌─────────────────┐
│  Windows Service │ ◄──────────────────────────► │   WinUI 3 GUI   │
│  (SYSTEM)        │    双向，JSON 消息           │   (普通用户)     │
│                  │                              │                  │
│  ┌─────────────┐ │         ┌─────────────┐     │                 │
│  │ RyzenAdj    │ │         │  SQLite     │     │                 │
│  │ (P/Invoke)  │ │         │  (WAL)      │     │                 │
│  └─────────────┘ │         └──────▲──────┘     │                 │
└─────────────────┘                │              └─────────────────┘
                                   │
                            共享读写
```

- **Named Pipe**：实时状态推送、模式切换命令、即时参数下发
- **SQLite (WAL)**：配置持久化、日志持久化、状态缓存（GUI 重启后可恢复状态显示）

---

## 2. Windows Service

### 2.1 生命周期

```
Service 启动
    │
    ├─ 检查 libryzenadj.dll / WinRing0x64.dll / WinRing0x64.sys 是否存在
    ├─ init_ryzenadj() → 获取 ryzen_access 句柄
    ├─ init_table()    → 初始化 PM Table
    ├─ 启动 Named Pipe Server
    ├─ 注册系统事件监听 (电源切换 / 系统唤醒)
    └─ 进入主循环
         │
         ├─ 读取当前模式 (SQLite)
         ├─ 计算目标参数
         ├─ set_*() 写入
         ├─ refresh_table() + get_*_limit() 验证
         ├─ 更新 SQLite 状态缓存
         ├─ 通过 Named Pipe 推送给 GUI
         └─ 等待 N 秒 (可配置，默认 4 秒)
```

### 2.2 模块职责

| 模块 | 文件 | 职责 |
|------|------|------|
| Worker | `Worker.cs` | BackgroundService 主循环，协调各模块 |
| ParameterApplier | `ParameterApplier.cs` | 封装单次 set + verify 逻辑，返回实际值 |
| ModeScheduler | `ModeScheduler.cs` | 自动模式状态机，判断何时切换 |
| SystemEventMonitor | `SystemEventMonitor.cs` | 监听电源/唤醒事件，触发即时下发 |
| PipeServer | `PipeServer.cs` | Named Pipe 服务端，消息收发 |

### 2.3 自动模式状态机

不考虑 AC/电池电源状态，仅根据**用户活动** + **CPU 负载**判断。

```
┌─────────────────────────────────────────────────────────────┐
│                       自动模式                               │
│                                                             │
│  性能模式 → 省电模式:                                        │
│    条件: 用户无输入 ≥ 5 分钟 且 CPU 负载 ≤ 10% 持续 5 分钟   │
│    防抖: 满足条件后延迟 1 分钟再切换                         │
│                                                             │
│  省电模式 → 性能模式:                                        │
│    条件: 用户有输入 或 CPU 负载 > 10%                        │
│    立即切换                                                  │
│                                                             │
│  任意切换后冷却 10 分钟                                      │
└─────────────────────────────────────────────────────────────┘

状态转移:
         ┌──────────────────────────────────────────────────┐
         │                                                  │
         ▼                                                  │
   ┌───────────┐  无输入≥5min 且 CPU≤10%持续5min  ┌────────┤
   │ 性能模式   │ ◄────────────────────────────── │ 候选省电│
   │           │                                  │(等1min)│
   └───────────┘                                  └───┬────┘
         │                                            │
         │  有输入 或 CPU>10%                         │ 确认
         │  (立即切换)                                │
         └────────────────────────────────────────────┘
```

**CPU 负载检测**：WMI `Win32_Processor.LoadPercentage`，每 5 秒采样一次，保留最近 1 分钟的采样值，所有采样 ≤ 10% 才判定为低负载。

**用户输入检测**：`GetLastInputInfo()` Win32 API，无侵入，Service 中每秒轮询一次。

### 2.4 系统事件响应

```csharp
// 监听电源事件
SystemEvents.PowerModeChanged += (s, e) =>
{
    if (e.Mode == PowerModes.Resume)
        TriggerImmediateApply();  // 从休眠/睡眠唤醒
};

// Windows 电源方案变更 (AC/DC 切换)
// 通过 RegisterPowerSettingNotification 注册 GUID_ACDC_POWER_SOURCE
// AC/DC 切换时立即触发参数下发 + 自动模式状态重评估
```

唤醒或电源切换时，跳过定时器等待，立即执行一次参数下发。

---

## 3. Named Pipe 通信协议

### 3.1 消息格式

```
[4 bytes: uint32 length (LE)] [N bytes: UTF-8 JSON]
```

### 3.2 消息类型

**Service → GUI：**

```jsonc
// 状态更新（每 N 秒推送一次）
{
  "Type": "StatusUpdate",
  "Mode": "Performance",
  "SetLimits": {
    "FastLimitW": 45.0,
    "SlowLimitW": 45.0,
    "TctlTempC": 90.0
  },
  "ActualLimits": {
    "FastLimitW": 30.0,    // 被 BIOS cap 了
    "SlowLimitW": 45.0,
    "TctlTempC": 90.0
  },
  "Metrics": {
    "CpuFreqMhz": 3200,
    "SocketPowerW": 28.5,
    "CpuTempC": 72
  }
}

// 服务状态变更
{
  "Type": "ServiceState",
  "IsRunning": true,
  "EngineVersion": "0.19.0",
  "CpuFamily": "PhoenixPoint"
}
```

**GUI → Service：**

```jsonc
// 切换模式
{ "Type": "SwitchMode", "Mode": "PowerSaving" }

// 立即应用（用户手动点击"立即应用"时）
{ "Type": "ApplyNow" }

// 请求当前状态
{ "Type": "RequestStatus" }
```

### 3.3 错误处理

- Named Pipe 断连：Service 自动等待 GUI 重连，状态写入 SQLite 作为 fallback
- 消息解析失败：记录日志，忽略该消息
- GUI 未启动：Service 独立运行，状态仅写入 SQLite

---

## 4. WinUI 3 GUI

### 4.1 页面结构

```
MainWindow
├── NavigationView (侧边栏)
│   ├── 首页      (Home)
│   ├── 设置      (Settings)
│   ├── 能效分析  (Profiler)
│   ├── 运行日志  (Logs)
│   └── 关于      (About)
│
└── Frame (内容区)
    ├── HomePage.xaml
    ├── SettingsPage.xaml
    ├── ProfilerPage.xaml
    ├── LogPage.xaml
    └── AboutPage.xaml
```

### 4.2 启动流程

```
App.OnLaunched()
    │
    ├─ 检查管理员权限
    │   └─ 非管理员 → ContentDialog 提示 + runas 重新启动 / 退出
    │
    ├─ 检查反作弊提示是否已确认 (anti_cheat_ack)
    │   └─ 未确认 → ContentDialog 提示一次
    │
    ├─ 检查 Service 是否运行
    │   └─ 未运行 → InfoBar 提示用户安装/启动 Service
    │
    ├─ 连接 Named Pipe
    ├─ 加载配置 (SQLite)
    └─ 导航到 HomePage
```

### 4.3 核心控件

**NumberBox**（带单位后缀）：
```xml
<NumberBox Value="{x:Bind ViewModel.FastLimit, Mode=TwoWay}"
           Minimum="10" Maximum="100" SpinButtonPlacementMode="Inline">
    <NumberBox.Header>
        <StackPanel Orientation="Horizontal">
            <TextBlock Text="Fast Limit" />
            <FontIcon Glyph="&#xE946;" FontFamily="Segoe Fluent Icons"
                      ToolService.ToolTip="峰值功耗上限..." />
        </StackPanel>
    </NumberBox.Header>
</NumberBox>
```

**StatCard**（状态展示卡片）：
```xml
<local:StatCard Label="CPU 温度" Value="{x:Bind ViewModel.CpuTemp}"
                Unit="℃" Icon="&#xEB91;" />
```

**日志级别图标**：
```xml
<FontIcon Glyph="&#xE946;" />  <!-- Info: CircleInformation -->
<FontIcon Glyph="&#xE7BA;" />  <!-- Warning: Warning -->
<FontIcon Glyph="&#xE783;" />  <!-- Error: ErrorBadge -->
```

---

## 5. libryzenadj.dll P/Invoke

### 5.1 使用的 API

```csharp
// 生命周期
IntPtr init_ryzenadj();
void   cleanup_ryzenadj(IntPtr ry);
int    get_cpu_family(IntPtr ry);

// 参数设置 (uint value 单位 mW 或 ℃)
int set_fast_limit(IntPtr ry, uint value);
int set_slow_limit(IntPtr ry, uint value);
int set_stapm_limit(IntPtr ry, uint value);   // = slow-limit
int set_tctl_temp(IntPtr ry, uint value);
int set_power_saving(IntPtr ry);
int set_max_performance(IntPtr ry);

// PM Table
int   init_table(IntPtr ry);
int   refresh_table(IntPtr ry);
float get_fast_limit(IntPtr ry);
float get_fast_value(IntPtr ry);
float get_slow_limit(IntPtr ry);
float get_slow_value(IntPtr ry);
float get_stapm_limit(IntPtr ry);
float get_stapm_value(IntPtr ry);
float get_tctl_temp(IntPtr ry);
float get_tctl_temp_value(IntPtr ry);
float get_socket_power(IntPtr ry);
float get_core_clk(IntPtr ry, uint core);
float get_core_temp(IntPtr ry, uint core);
```

### 5.2 线程安全

`ryzen_access` 句柄不是线程安全的。封装为 `RyzenAdjWrapper` 单例，所有调用通过 `lock(_lock)` 串行化。

### 5.3 PM Table 验证

每次 `set_*()` 后，必须调用 `refresh_table()` + `get_*_limit()` 读回实际值。

"成功"（返回 0）≠ "生效"。SMU 可能接受命令但被 OEM cap 截断。

验证逻辑：
- 设置值与实际值差异 > 1W → 记录 Warning 日志
- 差异信息通过 Named Pipe 推送给 GUI 显示

### 5.4 运行时依赖

| 文件 | 必需 | 说明 |
|------|------|------|
| `libryzenadj.dll` | 是 | RyzenAdj 核心库 |
| `WinRing0x64.dll` | 是 | Ring0 驱动接口 |
| `WinRing0x64.sys` | 是 | Ring0 内核驱动 |
| `inpoutx64.dll` | 否 | 不包含，可能被反作弊拦截 |

---

## 6. SQLite 数据库

### 6.1 并发策略

使用 WAL 模式，Service 写 + GUI 读可并发进行。

```csharp
var conn = new SQLiteConnection("Data Source=RyzenTunerNext.db;Version=3;Journal Mode=WAL;");
```

### 6.2 表结构概要

| 表 | 用途 | 写者 | 读者 |
|----|------|------|------|
| `Settings` | 配置 (KV) | GUI | Service |
| `Logs` | 运行日志 | Service | GUI |
| `StatusCache` | 当前状态快照 | Service | GUI |
| `ProfilerResults` | 能效分析结果 | GUI (调用 Service 执行) | GUI |

详见 `docs/RyzenTunerNext/DESIGN.md` 第 4 节。

---

## 7. 能效分析 (Profiler)

### 7.1 流程

```
用户点击"开始测试"
    │
    ├─ Service 进入 Profiler 模式（暂停自动模式的定时写入）
    ├─ 对每个功耗档位 (如 15W, 25W, 35W, 45W, 54W)：
    │   ├─ set_fast_limit / set_slow_limit / set_stapm_limit
    │   ├─ 等待 2 秒稳定
    │   ├─ 运行基准测试（多线程计算密集任务）
    │   ├─ 记录：成绩、实际功耗 (get_socket_power)、最高温度 (get_tctl_temp_value)
    │   └─ 写入 ProfilerResults 表
    ├─ 计算能效比 (Score / ActualPowerW)
    ├─ 生成建议（甜点功耗区间）
    └─ 恢复自动模式
```

### 7.2 基准测试方案

自写 C# 基准测试（零外部依赖），使用多线程素数计数（Eratosthenes 筛法变体）。

- `Parallel.For` 占满所有逻辑核心，无锁竞争（按区间分块独立筛选）
- 纯 CPU 计算密集型，无 I/O 依赖
- 场景是同一台机器不同功耗下的相对对比，不需要跨机器公信力

**为什么不用外部工具**：
- CPU-Z Benchmark 算法私有且不可嵌入
- Cinebench / Geekbench 需要额外打包大型 exe
- WinSAT 已在 Windows 11 移除

---

## 8. 系统托盘

### 8.1 托盘图标

WinUI 3 中使用 `H.NotifyIcon`（社区库）或 `Microsoft.UI.Xaml.TrayIcon` 实现系统托盘。

- 左键双击：恢复主窗口
- 右键菜单：Service 状态（只读）、当前模式（只读）、模式切换（自动/省电/性能）、打开主窗口、退出
- Tooltip：显示状态摘要（模式 + 功耗 + 温度）

### 8.2 最小化与恢复

| 场景 | 行为 |
|------|------|
| 用户点击关闭按钮 | 最小化到托盘，不退出 |
| 自动模式 + 电池 + 无输入 ≥ 5 分钟 | 自动最小化到托盘 |
| 用户有键盘/鼠标输入 | 自动恢复窗口 |
| 右键菜单 → 退出 | 真正退出程序 |

GUI 最小化到托盘时，Service 继续独立运行，不受影响。

---

## 9. 错误处理策略

| 场景 | 处理 |
|------|------|
| `init_ryzenadj()` 返回 NULL | 记录 Error 日志，每 30 秒重试，GUI 显示 Service 异常状态 |
| `set_*()` 返回负数 | 记录 Warning 日志，附带错误描述，继续执行 |
| PM Table 验证不通过 | 记录 Warning 日志（设置值 vs 实际值），GUI 用 InfoBar 提示 |
| WinRing0 驱动加载失败 | 记录 Error 日志，提示检查杀毒软件/Secure Boot |
| Named Pipe 断连 | Service 等待重连，GUI 自动重连（指数退避） |
| SQLite 写入失败 | 记录日志，不影响参数下发（核心功能不依赖 DB） |

---

## 9. 待定问题

| # | 问题 | 备注 |
|---|------|------|
| 1 | Profiler 基准测试引擎选择 | 方案 A/B/C 各有取舍 |
| 2 | Service 安装方式 | `sc.exe` 手动 vs GUI 内按钮 |
| 3 | 全局快捷键注册方 | GUI 注册更简单，但 GUI 可能未启动 |
| 4 | 多实例保护 | Service 需要 Mutex 防止重复启动 |
