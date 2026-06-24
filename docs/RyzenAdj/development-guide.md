# RyzenAdj 开发指南

> 适用于 RyzenAdj **v0.19.0**。本文档面向需要理解内部实现或进行二次开发的工程师。

## 一、项目架构

### 1.1 目录结构

```
RyzenAdj/
├── main.c                  # CLI 主程序
├── argparse.c/h            # 第三方命令行参数解析库（Yecheng Fu, MIT 许可证）
├── CMakeLists.txt          # 构建配置
├── lib/                    # 核心库 (libryzenadj)
│   ├── ryzenadj.h          # 公共 API 头文件
│   ├── ryzenadj_priv.h     # 内部数据结构（私有）
│   ├── api.c               # API 实现（参数设置 + PM Table 读取）
│   ├── cpuid.c             # CPUID 检测
│   ├── nb_smu_ops.c/h      # SMU 通信协议
│   ├── linux/              # Linux 平台后端
│   │   ├── osdep_linux.c           # 后端选择逻辑
│   │   ├── osdep_linux_mem.c       # /dev/mem + libpci 后端
│   │   └── osdep_linux_smu_kernel_module.c  # ryzen_smu 内核模块后端
│   └── win32/              # Windows 平台后端
│       └── osdep_win32.cpp # WinRing0 驱动后端
├── win32/                  # Windows 运行时文件
│   ├── WinRing0x64.dll/sys # 内核驱动
│   ├── inpoutx64.dll       # I/O 驱动（可选）
│   ├── readjustService.ps1 # 自动化脚本
│   └── installServiceTask.bat
└── examples/               # 示例代码
    ├── readjust.py         # 自动化调参示例
    └── pmtable-example.py  # PM Table 监控示例
```

### 1.2 核心组件

```
┌─────────────────────────────────────────────────────────────┐
│                        用户程序                              │
│         (ryzenadj CLI / Python ctypes / C 集成)             │
└──────────────────────────┬──────────────────────────────────┘
                           │ ryzenadj.h API
┌──────────────────────────▼──────────────────────────────────┐
│                    libryzenadj (api.c)                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐ │
│  │ 参数设置     │  │ PM Table    │  │ CPUID 检测          │ │
│  │ set_*()     │  │ get_*()     │  │ cpuid_get_family()  │ │
│  └──────┬──────┘  └──────┬──────┘  └─────────────────────┘ │
└─────────┼────────────────┼──────────────────────────────────┘
          │                │
┌─────────▼────────────────▼──────────────────────────────────┐
│                  SMU 通信层 (nb_smu_ops.c)                   │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  smu_service_req() — SMU 请求/响应协议                   ││
│  │  get_smu() — 获取 MP1 SMU / PSMU 实例                   ││
│  └─────────────────────────────────────────────────────────┘│
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                   平台抽象层 (OS-dependent)                  │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ Linux            │  │ Windows          │                 │
│  │ ├─ /dev/mem      │  │ └─ WinRing0      │                 │
│  │ └─ ryzen_smu kmod│  │    (inpoutx64)   │                 │
│  └──────────────────┘  └──────────────────┘                 │
└─────────────────────────────────────────────────────────────┘
```

### 1.3 数据流

**参数设置流程：**
```
用户调用 set_xxx(ry, value)
    ↓
api.c: 根据 CPU family 选择 SMU 命令 ID
    ↓
nb_smu_ops.c: smu_service_req()
    ↓
平台层: smn_reg_write() 写入 SMU 寄存器
    ↓
等待 SMU 响应寄存器变化
    ↓
返回响应码 (OK / UnknownCmd / Rejected / Failed)
```

**PM Table 读取流程：**
```
init_table(ry)
    ↓
request_table_ver_and_size() → 向 PSMU 查询版本和大小
    ↓
request_table_addr() → 向 PSMU 获取物理地址
    ↓
init_mem_obj() → 映射物理内存
    ↓
refresh_table()
    ↓
request_transfer_table() → 请求 PSMU 将数据传输到内存
    ↓
copy_pm_table() → 从物理内存复制到用户缓冲区
    ↓
get_xxx() → 从缓冲区按偏移量读取 float 值
```

## 二、SMU 通信协议

### 2.1 SMU 概述

AMD Ryzen 处理器内置两个 SMU（System Management Unit）实例：

| SMU | 类型 | 用途 |
|-----|------|------|
| MP1 SMU | 主要 | 处理大部分功耗/温度/电流调整 |
| PSMU (RSMU) | 次要 | 处理 PM Table 操作和部分特殊调整 |

### 2.2 寄存器地址

SMU 通过 MMIO 寄存器进行通信，地址因 CPU 代际而异：

**MP1 SMU 寄存器：**

| 系列 | Message 地址 | Response 地址 | Args 基地址 |
|------|-------------|---------------|-------------|
| Raven/Picasso/Dali/Renoir/Cezanne | 0x3B10528 | 0x3B10564 | 0x3B10998 |
| Rembrandt/VanGogh/Mendocino/Phoenix/HawkPoint | 0x3B10528 | 0x3B10578 | 0x3B10998 |
| KrackanPoint/StrixPoint/StrixHalo | 0x3B10928 | 0x3B10978 | 0x3B10998 |
| DragonRange/FireRange | 0x3B10530 | 0x3B1057C | 0x3B109C4 |

**PSMU 寄存器：**

| 系列 | Message 地址 | Response 地址 | Args 基地址 |
|------|-------------|---------------|-------------|
| 大部分系列 | 0x3B10A20 | 0x3B10A80 | 0x3B10A88 |
| DragonRange/FireRange | 0x3B10524 | 0x3B10570 | 0x3B10A40 |

### 2.3 通信协议

SMU 通信遵循以下协议：

```
1. 清除响应寄存器 (写入 0x0)
2. 写入参数到 Args 寄存器 (arg0-arg5)
3. 写入命令 ID 到 Message 寄存器
4. 轮询 Response 寄存器直到非 0
5. 读取返回参数 (arg0-arg5)
```

### 2.4 响应码

| 值 | 常量 | 含义 |
|----|------|------|
| 0x01 | `REP_MSG_OK` | 成功 |
| 0xFF | `REP_MSG_Failed` | 失败 |
| 0xFE | `REP_MSG_UnknownCmd` | 命令不支持 |
| 0xFD | `REP_MSG_CmdRejectedPrereq` | 前置条件不满足 |
| 0xFC | `REP_MSG_CmdRejectedBusy` | SMU 忙碌 |

### 2.5 SMU 命令 ID

每个 `set_*` 函数内部使用特定的 SMU 命令 ID。命令 ID 因 CPU family 而异。例如 `set_stapm_limit`：

| Family | MP1 SMU 命令 | PSMU 命令（备选） |
|--------|-------------|------------------|
| Raven/Picasso/Dali | 0x1A | — |
| Renoir ~ StrixHalo | 0x14 | 0x31 (PSMU fallback) |
| DragonRange/FireRange | 0x4F | — |

> 完整的命令 ID 映射表请参考 `lib/api.c` 源代码。

## 三、PM Table 详解

### 3.1 概述

PM Table 是 SMU 维护的内存映射数据结构，包含实时功耗、温度、电流、频率等指标。布局随 table version 不同而变化。

### 3.2 初始化流程

```c
// 1. 获取 table version 和 size
smu_service_req(psmu, get_table_ver_msg, &args);
// args.arg0 = table_version
// table_size 由 version 查表确定

// 2. 获取物理地址
smu_service_req(psmu, get_table_addr_msg, &args);
// args.arg0 = physical_address (32-bit)
// Rembrandt+: args.arg1:arg0 = 64-bit address

// 3. 映射物理内存
init_mem_obj(os_access, table_addr);

// 4. 传输数据
smu_service_req(psmu, transfer_table_msg, &args);
// SMU 将 PM Table 数据写入指定物理地址

// 5. 复制到用户缓冲区
memcpy(buffer, phy_map, table_size);
```

### 3.3 版本与大小映射

| Version | Size | 对应系列 |
|---------|------|---------|
| 0x1E0001 | 0x568 | Raven |
| 0x1E0002 | 0x580 | Raven (新固件) |
| 0x1E0003 | 0x578 | Picasso |
| 0x1E0004 - 0x1E0101 | 0x608 | Picasso/Dali |
| 0x370000 | 0x794 | Renoir |
| 0x370001 | 0x884 | Renoir (新固件) |
| 0x370002 | 0x88C | Renoir (新固件) |
| 0x370003 - 0x370004 | 0x8AC | Renoir/Lucienne/Cezanne |
| 0x370005 | 0x8C8 | Cezanne |
| 0x3F0000 | 0x7AC | Van Gogh |
| 0x400001 | 0x910 | Rembrandt |
| 0x400002 | 0x928 | Rembrandt |
| 0x400003 | 0x94C | Rembrandt |
| 0x400004 - 0x400005 | 0x944 | Rembrandt |
| 0x450004 - 0x450005 | 0xAA4-0xAB0 | Mendocino |
| 0x4C0003 - 0x4C0009 | 0xAF0-0xB1C | Phoenix/HawkPoint |
| 0x5D0008 - 0x5D000B | 0xD54 | StrixPoint |
| 0x64020C | 0xE50 | StrixHalo |
| 0x650005 | (kmod) | KrackanPoint |

> 未知版本使用 0x1000 作为哨兵值，实际大小由 ryzen_smu 内核模块提供。

### 3.4 固定偏移量（前 24 字节）

以下 6 个 float 值在所有 PM Table 版本中位置固定：

| 偏移量 | 长度 | 含义 | API 函数 |
|--------|------|------|----------|
| 0x00 | 4 | STAPM Limit | `get_stapm_limit()` |
| 0x04 | 4 | STAPM Value | `get_stapm_value()` |
| 0x08 | 4 | PPT FAST Limit | `get_fast_limit()` |
| 0x0C | 4 | PPT FAST Value | `get_fast_value()` |
| 0x10 | 4 | PPT SLOW Limit | `get_slow_limit()` |
| 0x14 | 4 | PPT SLOW Value | `get_slow_value()` |

### 3.5 可变偏移量示例

其余偏移量因 table version 而异。以 Renoir (0x370004) 为例：

| 偏移量 | 含义 |
|--------|------|
| 0x18 | APU PPT SLOW Limit |
| 0x1C | APU PPT SLOW Value |
| 0x20 | TDC VDD Limit |
| 0x24 | TDC VDD Value |
| 0x28 | TDC SoC Limit |
| 0x2C | TDC SoC Value |
| 0x30 | EDC VDD Limit |
| 0x34 | EDC VDD Value |
| 0x38 | EDC SoC Limit |
| 0x3C | EDC SoC Value |
| 0x40 | Tctl Temp Limit |
| 0x44 | Tctl Temp Value |
| ... | ... |

> 完整偏移量映射请参考 `lib/api.c` 中的 `get_*` 函数实现。

### 3.6 StrixPoint 特殊说明

StrixPoint (0x5D0008-0x5D000B) 使用不同的偏移量布局：

| 功能 | 旧版偏移 | StrixPoint 偏移 |
|------|---------|----------------|
| TDC VDD Limit | 0x20 | 0x30 |
| TDC VDD Value | 0x24 | 0x34 |
| TDC SoC Limit | 0x28 | 0x38 |
| TDC SoC Value | 0x2C | 0x3C |
| Tctl Temp Limit | 0x40 | 0x40 |
| Tctl Temp Value | 0x44 | 0x44 |

> 这是因为 StrixPoint 的 PM Table 前部有更多保留字段。

## 四、平台后端实现

### 4.1 Linux 后端

Linux 支持两种后端，运行时自动选择：

**ryzen_smu 内核模块后端（优先）：**
- 通过 `/sys/kernel/ryzen_smu_drv/` 接口访问
- 读取 PM Table: `pm_table` 文件
- SMN 寄存器读写: `smn` 文件
- 优点：更安全，不依赖 `/dev/mem`
- 最低版本要求：0.1.7

**/dev/mem + libpci 后端（回退）：**
- 通过 PCI 配置空间访问 NB 寄存器
- PM Table 通过 mmap 映射 `/dev/mem`
- 需要内核支持 `iomem=relaxed`
- 可能被 Secure Boot 阻止

### 4.2 Windows 后端

Windows 后端使用 C++ 实现（`osdep_win32.cpp`），这是因为 WinRing0 的头文件 (`OlsApi.h`) 使用了 C++ 调用约定。其余核心库代码均为 C。

Windows 使用 WinRing0 驱动：
- `WinRing0x64.dll` — 用户态 DLL
- `WinRing0x64.sys` — 内核驱动
- 通过 PCI 配置空间访问 NB 寄存器
- PM Table 通过物理内存映射访问

**可选组件：**
- `inpoutx64.dll` — 仅用于 `--info` 和 `--dump-table` 的辅助功能
- 可安全删除，不影响参数设置功能

## 五、构建系统

### 5.1 CMake 配置

```cmake
# 主要构建目标
ADD_EXECUTABLE(ryzenadj ...)        # CLI 可执行文件
ADD_LIBRARY(libryzenadj ...)        # 共享库

# 平台特定源文件
if(WIN32)
    set(OS_SOURCE lib/win32/osdep_win32.cpp)
    set(OS_LINK_LIBRARY WinRing0x64)
elseif(Linux)
    set(OS_SOURCE lib/linux/osdep_linux.c
                  lib/linux/osdep_linux_mem.c
                  lib/linux/osdep_linux_smu_kernel_module.c)
    set(OS_LINK_LIBRARY RyzenAdj::Libpci)
endif()

# 核心源文件（跨平台）
set(COMMON_SOURCES lib/nb_smu_ops.c lib/api.c lib/cpuid.c)
```

### 5.2 编译选项

| 选项 | 默认值 | 说明 |
|------|--------|------|
| `BUILD_SHARED_LIBS` | ON | 构建共享库 |
| `PREFER_STATIC_LINKING` | OFF | 静态链接 ryzenadj 可执行文件 |
| `CMAKE_BUILD_TYPE` | — | Release 推荐启用 LTO |

### 5.3 符号可见性

库使用 `-fvisibility=hidden` 编译，仅导出标记为 `EXP` 的函数。这确保了 ABI 稳定性。

## 六、错误处理

### 6.1 错误码层次

```
ADJ_ERR_FAM_UNSUPPORTED (-1)  → CPU family 不支持
ADJ_ERR_SMU_TIMEOUT (-2)      → SMU 响应超时（理论上不会发生，见下文说明）
ADJ_ERR_SMU_UNSUPPORTED (-3)  → SMU 返回 UnknownCmd
ADJ_ERR_SMU_REJECTED (-4)     → SMU 拒绝（前置条件/忙碌/失败）
ADJ_ERR_MEMORY_ACCESS (-5)    → 内存访问失败
```

> **关于 ADJ_ERR_SMU_TIMEOUT**：`smu_service_req()` 使用 `while(response == 0x0)` 轮询，**没有超时上限**。正常情况下 SMU 会在微秒级响应，但如果 SMU 硬件异常或寄存器映射错误，会导致无限循环。此错误码当前仅作为理论保留，实际不会被触发。

### 6.2 PM Table 错误恢复

PM Table 操作有特殊的错误恢复逻辑：

1. **空表检测**：Raven/Picasso 首次启动可能返回空数据，库自动重试
2. **传输拒绝**：SMU 可能拒绝连续的 transfer 请求，库会等待后重试
3. **大小不匹配**：ryzen_smu 内核模块的大小优先于 SMU 查询的大小

## 七、测试与调试

### 7.1 调试输出

编译时定义 `NDEBUG` 宏可禁用调试输出。默认情况下，`DBG()` 宏输出到 stderr。

### 7.2 PM Table 调试

使用 `--dump-table` 查看完整 PM Table：

```bash
sudo ryzenadj --dump-table
```

输出格式：
```
| Offset |    Data    |   Value   |
|--------|------------|-----------|
| 0x0000 | 0x41C80000 |    25.000 |  # STAPM Limit
| 0x0004 | 0x41460000 |    12.375 |  # STAPM Value
...
```

### 7.3 常见问题排查

1. **SMU 返回 UnknownCmd**：检查 CPU family 是否正确识别，命令 ID 是否匹配
2. **SMU 返回 Rejected**：可能是前置条件不满足（如未启用 OC 模式就设置 OC 参数）
3. **PM Table 全为 0**：可能是首次传输失败，检查是否有重试逻辑
4. **值设置成功但不生效**：可能是厂商 cap 限制，用 `--info` 验证实际值

## 八、扩展指南

### 8.1 添加新 CPU 支持

1. 在 `cpuid.c` 中添加 CPUID 识别逻辑
2. 在 `ryzenadj.h` 中添加 `FAM_*` 枚举值
3. 在 `nb_smu_ops.c` 中配置 SMU 寄存器地址
4. 在 `api.c` 中为每个 `set_*` 函数添加新 family 的 case
5. 在 `api.c` 中为每个 `get_*` 函数添加新 table version 的偏移量
6. 在 `main.c` 的 `family_name()` 中添加名称映射

### 8.2 添加新参数

1. 在 `main.c` 中添加命令行参数定义
2. 在 `ryzenadj.h` 中声明 `set_*` 函数
3. 在 `api.c` 中实现 `set_*` 函数，包含所有 family 的命令 ID
4. 如需读取，在 `api.c` 中实现 `get_*` 函数，包含所有 table version 的偏移量
5. 更新文档

### 8.3 添加新平台后端

1. 创建 `lib/<platform>/osdep_<platform>.c`
2. 实现以下函数：
   - `init_os_access_obj()` — 初始化平台访问对象
   - `init_mem_obj()` — 初始化内存映射
   - `smn_reg_read/write()` — SMN 寄存器读写
   - `copy_pm_table()` — PM Table 数据复制
   - `free_os_access_obj()` — 清理资源
3. 在 `CMakeLists.txt` 中添加平台条件编译

## 九、许可证

RyzenAdj 使用 LGPL 许可证。这意味着：
- 可以在闭源软件中链接 libryzenadj
- 对 libryzenadj 的修改必须开源
- 静态链接需要提供目标文件以便重新链接

---

相关文档：
- [RyzenAdj 参数详解](ryzenadj-parameters.md) — 全部参数说明、各代 CPU 支持情况、常见陷阱
- [libryzenadj API 参考](libryzenadj-api.md) — C 库接口、PM Table 读取、集成示例
- [构建与使用指南](build-and-usage.md) — 安装、编译、命令行用法、自动化配置
