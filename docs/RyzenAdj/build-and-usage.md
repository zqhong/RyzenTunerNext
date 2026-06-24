# 构建与使用指南

> 适用于 RyzenAdj **v0.19.0**。

## 一、概述

RyzenAdj 是一个命令行工具，通过写入 SMU（System Management Unit）来实时调整 AMD Ryzen 移动平台的功耗、温度、电流等限制参数。

**核心特性：**
- 设置**重启后不保留**，每次开机恢复出厂值
- 电源模式切换（拔插电源线等）也会重置参数
- **必须以 root（Linux）或管理员（Windows）权限运行**

## 二、安装与构建

### Linux 构建

#### 依赖安装

```bash
# Debian/Ubuntu
sudo apt install build-essential cmake libpci-dev

# Fedora
sudo dnf install cmake gcc-c++ pciutils-devel

# Arch Linux
sudo pacman -S base-devel pciutils cmake

# openSUSE Tumbleweed
sudo zypper in cmake gcc14-c++ pciutils-devel
```

#### 编译

```bash
git clone https://github.com/FlyGoat/RyzenAdj.git
cd RyzenAdj
rm -r win32
mkdir build && cd build
cmake -DCMAKE_BUILD_TYPE=Release ..
make

# 安装到 /usr/local/bin/（systemd/udev 自动化需要）
sudo make install

# 或手动创建符号链接
if [ -d ~/.local/bin ]; then
    ln -s $(readlink -f ryzenadj) ~/.local/bin/ryzenadj
fi
```

#### Linux 后端选择

RyzenAdj 需要访问 NB config space，有两种后端：

| 后端 | 说明 | 优先级 |
|------|------|--------|
| `ryzen_smu` 内核模块 | 通过 sysfs 访问，更安全 | **优先使用** |
| `/dev/mem` + libpci | 直接内存映射，需内核支持 `iomem=relaxed` | 自动回退 |

RyzenAdj 启动时自动检测：优先尝试 `ryzen_smu`，不存在则回退到 `/dev/mem`。

#### 安装 ryzen_smu 内核模块（推荐）

```bash
git clone https://github.com/amkillam/ryzen_smu
cd ryzen_smu && sudo make dkms-install

# 如启用了 Secure Boot，需注册 MOK 密钥
sudo mokutil --import /var/lib/dkms/mok.pub
# 重启后在 MOK Manager 中选择 Enroll MOK 并输入密码
```

> 最低支持版本：ryzen_smu 0.1.7

### Windows 构建

> 推荐直接使用预编译的 Release 包，自行构建需要 Visual Studio + MSVC 环境。

**构建环境要求：**
- Visual Studio（MSVC）或 Clang + NMake
- **不支持 MinGW-gcc**

**运行时依赖：**
- `WinRing0x64.dll` / `WinRing0x64.sys` — 内核驱动
- `inpoutx64.dll` — I/O 驱动（可选，仅 `--info` 和 `--dump-table` 需要）

这些文件位于源码 `win32/` 目录下，需与 `ryzenadj.exe` 放在同一目录。

## 三、命令行用法

### 基本语法

```
ryzenadj [options]
```

### 查看信息

```bash
# 查看 CPU 型号及关键功耗指标
sudo ryzenadj --info

# 查看完整 PM Table（调试用）
sudo ryzenadj --dump-table
```

`--info` 输出示例：

```
CPU Family: Renoir
SMU BIOS Interface Version: 5
Version: v0.19.0
PM Table Version: 370004
|        Name         |   Value   |     Parameter      |
|---------------------|-----------|--------------------|
| STAPM LIMIT         |    25.000 | stapm-limit        |
| STAPM VALUE         |    12.345 |                    |
| PPT LIMIT FAST      |    35.000 | fast-limit         |
| PPT VALUE FAST      |    28.900 |                    |
...
```

### 设置参数

```bash
# 设置功耗限制（单位：mW）
sudo ryzenadj --fast-limit=45000 --slow-limit=35000 --stapm-limit=25000

# 设置温度上限（单位：°C）
sudo ryzenadj --tctl-temp=85

# 设置电流限制（单位：mA）
sudo ryzenadj --vrm-current=45000 --vrmmax-current=60000

# 组合使用
sudo ryzenadj --fast-limit=45000 --slow-limit=35000 --tctl-temp=85 --vrmmax-current=60000

# 查看设置结果
sudo ryzenadj --info
```

### 常用场景示例

**提升游戏性能：**
```bash
sudo ryzenadj --fast-limit=54000 --slow-limit=45000 --tctl-temp=90 --vrmmax-current=70000
```

**省电模式（轻负载/电池）：**
```bash
sudo ryzenadj --fast-limit=15000 --slow-limit=10000 --tctl-temp=70 --power-saving
```

**解锁 iGPU 性能：**
```bash
sudo ryzenadj --vrmmax-current=60000 --fast-limit=45000
# 如需调整 iGPU 频率（Zen2+）：
sudo ryzenadj --gfx-clk=1800
```

**曲线优化器降压（Zen3+）：**
```bash
sudo ryzenadj --set-coall=-10  # 负值 = 降压/省电，正值 = 加压/稳定。值需逐步测试稳定性
```

## 四、自动化配置（Windows）

由于设置重启后不保留，需要定期重新应用。

### 方法 1：Windows 计划任务（推荐）

1. 编辑 `win32/readjustService.ps1`，在其中配置你的 RyzenAdj 参数
2. 以管理员身份测试脚本确保正常工作
3. 运行 `win32/installServiceTask.bat` 安装为计划任务

```powershell
# readjustService.ps1 中的配置示例
$ryzenadjPath = "C:\path\to\ryzenadj.exe"
$ryzenadjArgs = "--fast-limit=45000 --slow-limit=35000 --tctl-temp=85"
```

**管理计划任务：**
```bash
# 查看任务状态
SCHTASKS /query /TN "AMD\RyzenAdj"

# 卸载任务
uninstallServiceTask.bat
```

### 方法 2：Linux systemd 服务

创建 systemd 服务文件 `/etc/systemd/system/ryzenadj.service`：

```ini
[Unit]
Description=RyzenAdj Power Management
After=multi-user.target

[Service]
Type=oneshot
ExecStart=/usr/local/bin/ryzenadj --fast-limit=45000 --slow-limit=35000 --tctl-temp=85
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now ryzenadj.service
```

### 方法 3：udev 规则（电源插拔时触发）

创建规则文件 `/etc/udev/rules.d/99-ryzenadj.rules`：

```bash
SUBSYSTEM=="power_supply", ATTR{type}=="Mains", ATTR{online}=="1", RUN+="/usr/local/bin/ryzenadj --fast-limit=45000 --slow-limit=35000"
SUBSYSTEM=="power_supply", ATTR{type}=="Mains", ATTR{online}=="0", RUN+="/usr/local/bin/ryzenadj --fast-limit=25000 --slow-limit=15000 --power-saving"
```

> **注意**：`/usr/local/bin/ryzenadj` 需要先通过 `sudo make install` 安装，或手动将编译产物复制到该路径。udev 规则中的 `RUN+` 命令必须使用绝对路径。

```bash
# 加载新规则
sudo udevadm control --reload-rules
sudo udevadm trigger
```

## 五、Python 调用示例

RyzenAdj 提供 `libryzenadj` 共享库，可通过 Python ctypes 调用。

### 基本调用

```python
import ctypes

# 加载库
lib = ctypes.CDLL("libryzenadj.so")  # Linux
# lib = ctypes.CDLL("libryzenadj.dll")  # Windows

# 初始化
ry = lib.init_ryzenadj()
if not ry:
    print("初始化失败，请检查权限")
    exit(1)

# 设置功耗限制
lib.set_fast_limit(ry, 45000)  # PPT FAST (mW)
lib.set_slow_limit(ry, 35000)  # PPT SLOW (mW)
lib.set_tctl_temp(ry, 85)      # Tctl 温度 (°C)
# 注意：set_stapm_limit 在 Zen2+ 上默认被 STTv2 覆盖，优先使用 fast-limit/slow-limit

# 读取 PM Table
lib.init_table(ry)
lib.refresh_table(ry)

# 读取指标（返回 float）
stapm_limit = lib.get_stapm_limit(ry)
stapm_value = lib.get_stapm_value(ry)
print(f"STAPM: {stapm_value:.1f} / {stapm_limit:.1f} W")

# 清理
lib.cleanup_ryzenadj(ry)
```

### PM Table 监控循环

```python
import ctypes
import time

lib = ctypes.CDLL("libryzenadj.so")
ry = lib.init_ryzenadj()
lib.init_table(ry)

while True:
    lib.refresh_table(ry)
    print(f"Socket Power: {lib.get_socket_power(ry):.1f} W")
    print(f"Tctl: {lib.get_tctl_temp_value(ry):.1f} °C")
    print(f"STAPM: {lib.get_stapm_value(ry):.1f} / {lib.get_stapm_limit(ry):.1f} W")
    print("---")
    time.sleep(2)
```

> 完整示例见 RyzenAdj 仓库的 `examples/readjust.py` 和 `examples/pmtable-example.py`。

## 六、常见错误与解决

详见 [RyzenAdj 参数详解 — 常见错误与解决](ryzenadj-parameters.md#六常见错误与解决)。以下为快速排查：

| 错误信息 | 原因 | 解决方案 |
|---------|------|---------|
| `Unable to init ryzenadj` | 权限不足或后端不可用 | 以 root/管理员运行；Linux 检查 `/dev/mem` 或 ryzen_smu 模块 |
| `pcilib: sysfs_write: write failed: Operation not permitted` | Secure Boot 阻止了 /dev/mem 访问 | 关闭 Secure Boot，或使用 ryzen_smu 内核模块 |
| `WinRing0 Err: 0x2 Unable to get PCI Obj` | Windows 驱动问题 | 以管理员运行；检查 Windows Defender → 设备安全 → 核心隔离 → 内存完整性 |
| `set_xxx is not supported on this family` | 参数不支持当前 CPU | 查看[参数文档](ryzenadj-parameters.md)确认支持范围 |
| `set_xxx is rejected by SMU` | SMU 拒绝了请求 | 可能被厂商 cap 限制，或前置条件不满足 |

## 七、注意事项

1. **"成功"不等于"生效"**：SMU 可能返回成功但值被厂商上限截断（cap）。必须用 `--info` 验证实际值。
2. **Prochot 是最大的坑**：`--tctl-temp` 过高会触发 prochot，CPU 功耗瞬间降到 4W。必须比 prochot 温度低 5-10°C。
3. **电压调整有限**：常规参数无法调整电压，但 `--oc-volt`（仅 Lucienne/Renoir/Cezanne）可设置核心 VID，曲线优化器（`--set-coall`/`--set-coper`）可间接影响电压（负值降压，正值加压）。
4. **不支持桌面平台**：仅支持 Ryzen Mobile/APU 系列（Dragon Range/Fire Range 是桌面级移动处理器，也受支持）。

---

相关文档：
- [RyzenAdj 参数详解](ryzenadj-parameters.md) — 全部参数说明、各代 CPU 支持情况、常见陷阱
- [libryzenadj API 参考](libryzenadj-api.md) — C 库接口、PM Table 读取、集成示例
- [开发指南](development-guide.md) — 项目架构、SMU 协议、PM Table 详解、扩展指南
