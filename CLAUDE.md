# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

RyzenTunerNext 是 [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) 的 Windows GUI 前端，用于调节 AMD Ryzen 移动端处理器的功耗/温度参数（PPT Fast Limit、PPT Slow Limit/STAPM、Tctl 温度目标）。支持自动模式：根据用户活动和 CPU 负载在性能/省电档位间自动切换。

## 技术栈

- **语言**: C#（nullable reference types, implicit usings）
- **目标框架**: `net10.0-windows10.0.22621.0`（.NET 10, Windows 10 22H2+）
- **UI**: WinUI 3（Windows App SDK 1.8），Unpackaged 模式（非 MSIX）
- **架构**: 仅 x64
- **MVVM**: CommunityToolkit.Mvvm 8.4.2（使用 source generators: `[ObservableProperty]`, `[RelayCommand]`）
- **数据库**: SQLite (Microsoft.Data.Sqlite + Dapper)，WAL 模式
- **系统托盘**: H.NotifyIcon.WinUI 2.3.2
- **原生互操作**: P/Invoke 调用 `libryzenadj.dll`（RyzenAdj v0.19.0）

## 构建命令

```bash
dotnet restore RyzenTunerNext.sln
dotnet build RyzenTunerNext.sln -c Debug -p:Platform=x64
dotnet build RyzenTunerNext.sln -c Release -p:Platform=x64

# 发布（自包含单文件）
dotnet publish src/RyzenTunerNext.App/RyzenTunerNext.App.csproj -c Release -p:Platform=x64 -o publish/app
```

前置条件：Windows 10 22H2+, .NET 10 SDK, Visual Studio 2022 17.8+（需安装 ".NET 桌面开发" 和 "Windows App SDK" 工作负载）。运行时需要 AMD Ryzen 移动端处理器 + 管理员权限。

无测试项目，无测试框架。

## 架构要点

### 单进程架构（当前状态）

文档中描述的双进程架构（Windows Service + WinUI 3 GUI 通过 Named Pipe 通信）**已合并为单个 WinUI 3 进程**。`PowerManager` 直接在 App 进程内运行功耗管理循环。Core 中的 Named Pipe 基础设施（`PipeServer`, `PipeClient`, `PipeProtocol`）保留但未被 App 使用。

### 两个项目

**RyzenTunerNext.Core**（类库，无 UI 依赖）:
- `Models/` — 数据类型（`PowerProfile`, `EnergyMode`, `ApplyResult`, `CpuMetrics` 等）
- `Services/` — `RyzenAdjNative`（P/Invoke 声明）, `RyzenAdjWrapper`（线程安全单例，`lock` 串行化所有调用）, `BenchmarkEngine`, `NativeLibraryLoader`
- `Data/` — SQLite 仓储（Dapper）: `SettingsRepository`（KV 存储）, `LogRepository`, `StatusCacheRepository` 等
- `Messaging/` — Named Pipe 协议（保留，当前未使用）

**RyzenTunerNext.App**（WinUI 3 可执行文件）:
- `App.xaml.cs` — 入口：Mutex 单实例保护、管理员权限检查与 UAC 重启、SQLite 初始化、反作弊警告
- `MainWindow.xaml.cs` — NavigationView 侧边栏（5 页面）、系统托盘图标、窗口位置记忆、空闲自动最小化
- `Views/` — `HomePage`, `SettingsPage`, `ProfilerPage`, `LogPage`, `AboutPage`
- `ViewModels/` — CommunityToolkit.Mvvm ViewModel
- `Services/` — `PowerManager`（主循环）, `ParameterApplier`（参数计算 + RyzenAdj 调用 + BIOS cap 验证）, `ModeScheduler`（自动模式状态机：WMI CPU 负载 + Win32 `GetLastInputInfo` 空闲检测）, `SystemEventMonitor`

### 核心数据流

1. `PowerManager.MainLoopAsync` 后台线程运行
2. `ModeScheduler.EvaluateAsync()` 判断当前能效模式（自动模式下检查用户空闲时间和 CPU 负载）
3. `ParameterApplier` 从 SQLite 读取设置，构建 `PowerProfile`
4. `RyzenAdjWrapper.ApplyProfile()` 通过 P/Invoke 调用 libryzenadj.dll 设置功耗限制，再读回 PM Table 验证实际值
5. 通过 C# 事件（`StatusUpdated`, `StateChanged`）广播到 UI
6. 状态写入 SQLite `status_cache` 表（GUI 重启后可恢复）
7. 循环间隔默认 4 秒，每 100ms 检查一次"立即应用"信号

### 关键设计决策

- **自包含单文件发布**: 整个 .NET 运行时和所有 DLL 打包进一个压缩 exe
- **需要管理员权限**: RyzenAdj 需要加载 WinRing0x64 内核驱动（`app.manifest` 声明 `requireAdministrator`）
- **线程安全的 RyzenAdj 访问**: 所有 P/Invoke 调用通过 `RyzenAdjWrapper` 单例的 `lock(_lock)` 串行化
- **BIOS cap 检测**: 设置限制后读回 PM Table 实际值，差异 > 1W 时警告用户
- **`set_*()` 返回 0 ≠ 生效**: SMU 可能接受命令但被 OEM cap 截断，必须验证

### 原生依赖

位于 `lib/`，构建时复制到 `native/` 子目录：
- `libryzenadj.dll` — RyzenAdj 核心库
- `WinRing0x64.dll` + `WinRing0x64.sys` — Ring0 内核驱动
- `inpoutx64.dll` — 不包含在分发中（可能被反作弊软件标记）

`NativeLibraryLoader` 注册自定义 DLL 导入解析器并调用 `SetDllDirectory`，确保单文件发布模式下能找到这些依赖。

## 注意事项

- `docs/` 中的部分文档描述的是早期双进程架构，与当前实现有差异（如仍提到 `RyzenTunerNext.Service` 项目和 Named Pipe 通信）
- `WinRing0x64.sys` 是 Ring0 内核驱动，杀毒软件可能标记为可疑，开发时需加入白名单
- 运行时需关闭反作弊软件（Riot Vanguard、EasyAntiCheat 等），因为它们会阻止 WinRing0 驱动加载
