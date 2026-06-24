# 开发环境部署指南

本文档说明如何搭建 RyzenTunerNext 的开发环境。

## 前置要求

### 硬件

- **处理器**: AMD Ryzen 移动端处理器（必需，RyzenAdj 仅支持此平台）
- **架构**: x64

### 操作系统

- Windows 10 (版本 22621 / 22H2 及以上) 或 Windows 11
- 不支持 macOS 和 Linux 开发（WinUI 3 和 Windows Service 依赖 Windows 运行时）

### 必装软件

| 软件 | 版本 | 说明 |
|------|------|------|
| .NET 8.0 SDK | 8.0.x | [下载页面](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Visual Studio 2022 | 17.8+ | 推荐使用 Community 版即可；需要安装以下工作负载 |
| Git | 最新版 | 用于克隆代码仓库 |

### Visual Studio 2022 工作负载

安装 Visual Studio 2022 时，勾选以下组件：

- **.NET 桌面开发** (Desktop development with C#)
- **Windows App SDK** 相关组件（在"单个组件"标签页中搜索 `WinUI`）

### 可选工具

| 软件 | 说明 |
|------|------|
| JetBrains Rider | 可替代 Visual Studio 2022，项目已包含 `.idea/` 配置 |
| Visual Studio Code | 轻量编辑器，配合 C# Dev Kit 扩展可使用 |

---

## 获取代码

```bash
git clone https://github.com/zqhong/RyzenTunerNext.git
cd RyzenTunerNext
```

---

## 命令行构建

### 还原依赖

```bash
dotnet restore RyzenTunerNext.sln
```

### 构建（Debug）

```bash
dotnet build RyzenTunerNext.sln -c Debug -p:Platform=x64
```

### 构建（Release）

```bash
dotnet build RyzenTunerNext.sln -c Release -p:Platform=x64
```

### 发布

发布为独立部署（self-contained），目标机器无需安装 .NET 运行时：

```bash
# 发布 App
dotnet publish src/RyzenTunerNext.App/RyzenTunerNext.App.csproj -c Release -p:Platform=x64 -o publish/app

# 发布 Service
dotnet publish src/RyzenTunerNext.Service/RyzenTunerNext.Service.csproj -c Release -p:Platform=x64 -o publish/service
```

---

## 使用 Visual Studio 2022 开发

1. 打开 `RyzenTunerNext.sln`
2. 在解决方案配置中选择 **Debug** 或 **Release**
3. 在解决方案平台中选择 **x64**（项目仅支持 x64）
4. 右键解决方案 → 属性 → 启动项目 → 选择"多个启动项目"
   - `RyzenTunerNext.Service` 设为"启动"
   - `RyzenTunerNext.App` 设为"启动"
5. 按 F5 启动调试

> **注意**: App 启动时会请求管理员权限（UAC），这是因为 RyzenAdj 需要内核级驱动访问权限。

---

## 使用 Rider 开发

1. 打开项目根目录的 `RyzenTunerNext.sln`
2. 在 Run Configuration 中分别配置：
   - `RyzenTunerNext.App`（启动 WinUI 应用）
   - `RyzenTunerNext.Service`（启动服务）
3. 创建 Compound 同时运行两者

---

## 运行时注意事项

### 管理员权限

App 的 `app.manifest` 中声明了 `requestedExecutionLevel level="requireAdministrator"`，启动时会触发 UAC 提示。这是必要的，因为 RyzenAdj 需要加载 WinRing0x64 内核驱动。

### 杀毒软件

`lib/WinRing0x64.sys` 是一个 Ring0 内核驱动，部分杀毒软件可能将其标记为可疑文件。开发时需要将其添加到杀毒软件白名单。

### 反作弊警告

App 启动时会检测并警告用户关闭正在运行的反作弊软件（如 Riot Vanguard、EasyAntiCheat 等），因为这些软件会阻止 WinRing0 驱动加载。

### 数据库

Service 和 App 共享同一 SQLite 数据库（WAL 模式），数据库文件位于 Service 运行目录下。Service 负责写入，App 负责读取。

---

## 项目结构

```
RyzenTunerNext/
├── lib/                              # 原生库（RyzenAdj、WinRing0）
├── src/
│   ├── RyzenTunerNext.Core/          # 共享核心库
│   │   ├── Models/                   # 数据模型
│   │   ├── Services/                 # RyzenAdj 封装、基准测试
│   │   ├── Data/                     # SQLite 数据访问层
│   │   └── Messaging/               # Named Pipe IPC 协议
│   ├── RyzenTunerNext.Service/       # Windows Service
│   │   ├── Worker.cs                 # BackgroundService 主循环
│   │   ├── ParameterApplier.cs       # 参数应用逻辑
│   │   ├── ModeScheduler.cs          # 自动模式调度器
│   │   └── SystemEventMonitor.cs     # 系统电源事件监听
│   └── RyzenTunerNext.App/           # WinUI 3 界面
│       ├── Views/                    # 5 个页面
│       ├── ViewModels/               # MVVM ViewModel
│       └── Controls/                 # 自定义控件
├── docs/                             # 文档
└── .github/workflows/                # CI/CD
```

---

## 调试技巧

### 调试 Service

Service 是一个 Windows Worker Service，在本地开发时以控制台应用形式运行。直接在 `Program.cs` 的 `Main` 方法断点即可。

### 调试 Named Pipe 通信

1. 先启动 Service
2. 再启动 App
3. 两者通过 Named Pipe `RyzenTunerNext_Pipe` 通信，消息格式为 4 字节长度前缀 + UTF-8 JSON

### 查看日志

Service 的运行日志写入 SQLite 数据库的 `logs` 表，可通过 App 的"日志"页面查看。

---

## CI/CD

项目使用 GitHub Actions 进行持续集成：

- **build.yml**: 推送到任意分支时触发，构建 Debug + Release，发布 App 和 Service，生成 zip 包（保留 30 天）
- **pr-check.yml**: PR 到 `main` 时触发，同 build.yml 流程，带并发控制

---

## 常见问题

### 构建失败：找不到 Windows SDK

确保安装了 Visual Studio 2022 的"Windows App SDK"相关组件，或单独安装 [Windows SDK 10.0.22621](https://developer.microsoft.com/en-us/windows/downloads/windows-sdk/)。

### App 启动后白屏

检查是否以管理员权限运行。WinUI 3 Unpackaged 模式下部分功能需要提升权限。

### RyzenAdj 不工作

确认：
1. CPU 是 AMD Ryzen 移动端处理器
2. WinRing0x64 驱动未被杀毒软件拦截
3. 没有反作弊软件运行
