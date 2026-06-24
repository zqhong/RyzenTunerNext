# RyzenAdj 参数详解

## 一、RyzenAdj 是什么

RyzenAdj 是一个针对 **AMD Ryzen 移动平台**（笔记本）的功率调节工具，通过写入 SMU（System Management Unit）来实时调整 CPU 的功耗、温度、电流等限制参数。**不支持桌面平台**（桌面平台请用 BIOS 或 Ryzen Master）。

设置**重启后不保留**，每次开机恢复出厂值。电源模式切换（拔插电源线等）也会重置参数。

当前版本：**v0.19.0**

### 术语说明

- **cap**（封顶/上限锁定）：Zen3 起，厂商可对 PM Table 参数设置最大允许值。当你设置的值超过上限时，命令返回"成功"，但实际值被截断到厂商设定的上限。例如厂商将 `slow-limit` 上限设为 35W，你执行 `--slow-limit=40000`（40W），实际生效值为 35.001W。
- **STTv2**（Skin Temperature Tracking v2）：皮肤温度保护机制。Zen2/Zen3/Zen3+ 上启用时会覆盖 `--stapm-limit`，用温度相关参数来间接控制功耗。
- **PSMU / MP1 SMU**：两个 SMU 实例。MP1 SMU 处理大部分功耗调整；PSMU 处理 PM Table 操作和部分特殊调整（gfx_clk、OC、曲线优化器）。
- **Tctl**（T Control — 控制温度）：AMD Ryzen 处理器的主要温度传感器读数，用于风扇控制和热管理。早期 Ryzen（Zen/Zen+）上 Tctl = Tdie + 偏移量（如 1700X/1800X 有 +20°C 偏移，让风扇更早转动），较新 Ryzen（Zen 3/4/5）上 Tctl ≈ Tdie 基本无偏移。RyzenAdj 的 `--tctl-temp` 参数就是设置 Tctl 温度目标值。
  - **Tdie**（T Die — 芯片实际温度）：处理器芯片的真实物理温度。与 Tctl 的关系：早期有偏移（Tctl = Tdie + offset），新架构基本相同（Tctl ≈ Tdie）。
  - **Tctl/Tdie 的热管理层级**：Tctl 温度从低到高，CPU 依次触发不同级别的保护措施——首先是 **Prochot**（处理器过热信号，触发极端降功耗），其次是 **THERMTRIP**（最终保护，直接关机，无法恢复）。Prochot 是软件可控范围内的最终防线，THERMTRIP 是硬件层面的绝对底线。
- **Prochot**（Processor Hot — 处理器过热信号）：CPU 内置的硬件热保护机制，是最终安全防线。当 CPU 温度达到阈值（通常 95-105°C，取决于具体型号和厂商设定）时触发，CPU 功耗瞬间被强制降到约 4W，系统近乎冻结。**不受软件控制**，无法通过 RyzenAdj 设置或禁用。不同厂商/型号的 prochot 阈值不同，需要自己测试。`--tctl-temp` 设置过高、接近 prochot 阈值时会触发此机制。

## 二、全部 42 个设置参数的作用

### 调试/信息类（2个）

| 参数 | 作用 |
|------|------|
| `--info` | 显示 CPU 型号及关键功耗指标（调整前后均可查看） |
| `--dump-table` | 显示完整 PM Table，用于排查设置是否被正确应用或被覆盖 |

### 功耗限制类（核心）

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--fast-limit` | mW | **PPT FAST 限制** — 瞬时/峰值功耗上限。在 `--slow-time` 时间窗口内的短时功耗天花板 | 全系列 |
| `--slow-limit` | mW | **PPT SLOW 限制** — 平均功耗上限。长期持续功耗的天花板 | 全系列 |
| `--slow-time` | 秒 | 慢 PPT 时间常数，控制平均功耗的计算窗口。**值越大，boost 维持时间越长** | 全系列 |
| `--stapm-limit` | mW | **STAPM 持续功耗限制**（第3级）。**Zen2/Zen3 上默认被 STTv2 覆盖（STTv2 启用时无效）**；仅在 STTv2 被禁用时可用 | 全系列（Zen2+ 受 STTv2 影响） |
| `--stapm-time` | 秒 | STAPM 时间常数。**Renoir 上值甚至不会写入 PM Table**；STTv2 启用时也不会改变值 | 全系列（Zen2+ 部分无效） |
| `--apu-slow-limit` | mW | APU PPT 慢速限制（用于有独显的 A+A 平台）。**Renoir 上命令被接受、值可改变，但 usage power 始终报告为 0，通常无实际效果** | Renoir ~ StrixHalo |

**层级关系：Fast Limit > Slow Limit > STAPM Limit**

### 温度限制类

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--tctl-temp` | °C | **Tctl 温度目标**。建议比实际 prochot 低 5-10°C，否则触发 prochot 后 CPU 功耗骤降至 4W | 全系列 |
| `--apu-skin-temp` | °C | **APU 表面温度限制**（STTv2）。控制设备外壳温度。Renoir/Cezanne 上命令被接受但无实际效果，Rembrandt 及之后真正支持 | Renoir ~ HawkPoint（含 VanGogh/Mendocino，**不含** StrixPoint/KrackanPoint/StrixHalo） |
| `--dgpu-skin-temp` | °C | **独显表面温度限制**（STTv2）。仅对有独显的设备有用 | Renoir ~ StrixPoint（含 VanGogh/Mendocino，**不含** KrackanPoint/StrixHalo） |
| `--skin-temp-limit` | mW | **表面温度触发后的功耗限制值**。过温时应用到 STAPM，需低于 `--slow-limit` | Renoir ~ StrixHalo |

### 电流限制类

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--vrm-current` | mA | **TDC VDD** — 长时间 VRM 电流限制。仅在 PM Table 显示为瓶颈时调整 | 全系列 |
| `--vrmmax-current` | mA | **EDC VDD** — 峰值 VRM 电流限制。**对 GPU 性能影响更大** | 全系列 |
| `--vrmsoc-current` | mA | **TDC SoC** — SoC 长时间电流限制。SoC 很少成为瓶颈 | Raven ~ StrixHalo |
| `--vrmsocmax-current` | mA | **EDC SoC** — SoC 峰值电流限制 | Raven ~ StrixHalo |
| `--vrmgfx-current` | mA | **TDC GFX** — GPU VRM 电流限制 | **Van Gogh 专用** |
| `--vrmgfxmax-current` | mA | **EDC GFX** — GPU 峰值 VRM 电流限制 | **Van Gogh 专用** |
| `--vrmcvip-current` | mA | **TDC CVIP** — CVIP 电流限制 | **Van Gogh 专用** |
| `--psi0-current` | mA | PSI0 VDD 电流限制。**功能不明确，部分设备无效果** | Raven ~ Cezanne（含 Renoir/Lucienne） |
| `--psi0soc-current` | mA | PSI0 SoC 电流限制。同上 | Raven ~ Cezanne（含 Renoir/Lucienne） |
| `--psi3cpu-current` | mA | PSI3 CPU 电流限制 | **Van Gogh 专用** |
| `--psi3gfx-current` | mA | PSI3 GFX 电流限制 | **Van Gogh 专用** |

### 频率控制类

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--max-socclk-frequency` / `--min-socclk-frequency` | MHz | SoC 时钟频率范围 | Raven/Picasso/Dali |
| `--max-fclk-frequency` / `--min-fclk-frequency` | MHz | CPU-GPU 传输频率范围 | Raven/Picasso/Dali |
| `--max-vcn` / `--min-vcn` | MHz | Video Core Next（视频编解码引擎）频率范围 | Raven/Picasso/Dali |
| `--max-lclk` / `--min-lclk` | MHz | Data Launch Clock 频率范围 | Raven/Picasso/Dali |
| `--max-gfxclk` / `--min-gfxclk` | MHz | GFX（集成显卡）时钟频率范围 | Raven/Picasso/Dali/Lucienne |
| `--gfx-clk` | MHz | **强制 iGPU 时钟频率**（通过 PSMU 0x89）。与上面的 min/max 不同，这是直接设置目标频率 | Renoir ~ StrixHalo（含 VanGogh/Mendocino/KrackanPoint） |

> `--max-gfxclk` 等旧频率参数在 Zen2 及以后**全部不生效**。如需在 Zen2+ 上调整 iGPU 频率，请使用 `--gfx-clk`（通过 PSMU 0x89，Renoir ~ StrixHalo 均支持）。

### 超频控制类（Renoir 及以上）

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--oc-clk` | MHz | 强制核心时钟频率 | Lucienne/Renoir/Cezanne/Rembrandt（**不含** DragonRange/FireRange） |
| `--oc-volt` | — | 强制核心 VID。**需按公式换算：`(1.55 - 目标电压) / 0.00625`**，例如设置 1.25V → `(1.55-1.25)/0.00625 = 48` | Lucienne/Renoir/Cezanne（**不含 Rembrandt**） |
| `--enable-oc` | Flag | 启用 OC 模式 | Lucienne/Renoir/Cezanne/Rembrandt（**不含** Phoenix/HawkPoint/Strix 系列） |
| `--disable-oc` | Flag | 禁用 OC 模式 | Lucienne/Renoir/Cezanne/Rembrandt（**不含** Phoenix/HawkPoint/Strix 系列） |

> 注意：`--oc-volt` 是唯一能调整电压的参数，但它仅在 OC 模式下生效，且仅限 Lucienne/Renoir/Cezanne（**不含 Rembrandt**）。`--oc-clk` 同样仅限这三者 + Rembrandt，不支持 DragonRange/FireRange。

### 曲线优化器（Curve Optimizer）

| 参数 | 单位 | 作用 | 支持范围 |
|------|------|------|----------|
| `--set-coall` | — | 全核曲线优化器（负值 = 降压/省电，正值 = 加压/稳定） | Renoir/Lucienne/Cezanne, Rembrandt ~ StrixHalo（含 VanGogh/Mendocino）, DragonRange/FireRange |
| `--set-coper` | — | 逐核曲线优化器 | 同上 |
| `--set-cogfx` | — | iGPU 曲线优化器 | Renoir/Lucienne/Cezanne, Rembrandt/Vangogh/Phoenix/HawkPoint（**不含** KrackanPoint/StrixPoint/StrixHalo） |

> 曲线优化器是 RyzenAdj 中**唯一能间接影响电压的机制**。负值降低电压（省电降温但可能不稳定），正值增加电压（更稳定但功耗更高）。

### Prochot 与模式切换

| 参数 | 作用 |
|------|------|
| `--prochot-deassertion-ramp` | Prochot 解除后的功率恢复斜坡时间。值越大，prochot 后恢复越慢、限制越严格。实测参考：值 < 256 基本无效果；值 ~300 时 prochot 后功耗约 15W；值 ~20000 时 prochot 后功耗约 6W；限制至少持续 10 分钟以上。支持范围：全系列（含 Mendocino） |
| `--power-saving` | 省电模式（Flag）。Zen2 限制时钟到 2500 MHz，Zen+ 为 2400 MHz，约 10 秒后恢复。**拔电源时自动设置**，无需手动在电池模式下启用。在 AC 上使用可将空闲功耗从 4W 降到 2W，适合轻负载场景 |
| `--max-performance` | 性能模式（Flag）。与 `--power-saving` 互斥。**插电源时自动设置**。电池模式下使用可提升最高 50% 响应速度，但牺牲空闲续航。游戏场景因持续负载不受 boost delay 影响 |

## 三、核心参数（最常用的 5 个）

对于大多数用户，**真正需要关注的只有这几个**：

| 优先级 | 参数 | 理由 |
|--------|------|------|
| ⭐⭐⭐ | `--fast-limit` | 控制峰值功耗，直接影响短时爆发性能 |
| ⭐⭐⭐ | `--slow-limit` | 控制持续功耗，影响长时间高负载表现 |
| ⭐⭐⭐ | `--tctl-temp` | 温度目标，设置不当会触发 prochot 导致系统近乎冻结 |
| ⭐⭐ | `--slow-time` | 调节 boost 持续时长 |
| ⭐⭐ | `--vrmmax-current` | 电流瓶颈时的解锁，对 GPU 性能有帮助 |

其他参数按需使用：

- `--apu-skin-temp`：设备外壳过热时调（Rembrandt+ 真正生效）
- `--vrm-current`：PM Table 显示 TDC 为瓶颈时调
- `--max-performance` / `--power-saving`：电池/AC 模式切换场景
- `--stapm-limit`：**仅 Zen/Zen+ 有效**，Zen2/Zen3 默认被 STTv2 覆盖（STTv2 禁用时可用）
- `--gfx-clk`：调整 iGPU 频率（Zen2+ 替代旧 `--max-gfxclk`）
- `--set-coall`：曲线优化器，降压/降温的间接手段（Cezanne+）

## 四、重要注意事项

### 1. Prochot 是最大的坑

设置 `--tctl-temp` 过高会触发 prochot 安全机制，CPU 功耗瞬间降到 4W 以下，系统几乎冻结。**必须比 prochot 温度低 5-10°C**，且不同厂商的 prochot 阈值不同，需要自己测试。

### 2. 设置不是永久的

- 重启恢复默认
- 拔插电源线会重置
- 部分设备会周期性重置
- 需要用脚本或服务定期重新应用

### 3. "成功"不等于"生效"

SMU 会返回成功但实际可能被上限 cap 住了。**必须用 `--info` 或 `--dump-table` 验证实际值**。

### 4. Zen3 有厂商上限锁定（cap）

Cezanne/Rembrandt 的厂商可对 PM Table 参数设置最大允许值（cap）。所有调整都返回"成功"，但超过上限的值会被截断。已知被 cap 的案例：

| 参数 | cap 值 | 设备 |
|------|--------|------|
| `--fast-limit` | 30W | HP Probook 455 G8 / 5600U |
| `--slow-limit` | 25W | HP Probook 455 G8 / 5600U |
| `--vrm-current` | 60A | Lenovo Legion 7 / 5900HX |
| `--stapm-limit` | 65W | 天钡 MACO / 6850H（可在 BIOS 中修改上限） |

**STAPM Workaround**：如果 STTv2 被禁用（STAPM 可用）且厂商 cap 值不够高，可通过以下步骤定期重置 STAPM 使用量，使 STAPM 限制永远不被触发：

1. 将 STAPM limit 降低 5W
2. 将 STAPM duration 设为 0
3. 等待 10ms（让 0 清除已记录的功耗历史）
4. 将 STAPM duration 恢复为最大值（通常 500）
5. 将 STAPM limit 恢复为最大值

整个过程约 15-25ms。`readjustService.ps1` 中启用 `$resetSTAPMUsage = $true` 可自动执行（约每 2-3 分钟一次）。

### 5. STTv2 在 Zen2/Zen3 上是关键

STTv2（皮肤温度保护）会覆盖 `--stapm-limit`，并用 `--fast-limit` 或 `--skin-temp-limit` 来限功耗。如果调了 STAPM 没效果，大概率是 STTv2 在起作用。

STTv2 仅存在于 Zen2/Zen3/Zen3+。skin-temp 相关参数在 Renoir/Cezanne 上命令被接受但无实际效果，Rembrandt 及之后真正支持。具体范围：`--apu-skin-temp` 支持到 HawkPoint（含 VanGogh/Mendocino，不含 KrackanPoint/StrixPoint/StrixHalo），`--dgpu-skin-temp` 支持到 StrixPoint（含 VanGogh/Mendocino，不含 KrackanPoint/StrixHalo），`--skin-temp-limit` 支持到 StrixHalo。

### 6. 功耗限制是分层的

实际功耗受多层限制约束：温度、TDC、EDC、PPT FAST、PPT SLOW、STAPM、皮肤温度——**任何一层都可能成为瓶颈**。需要用 `--info` 在满载时检查具体是哪个限制在起作用。

### 7. 频率控制参数在 Zen2+ 上已失效

`--max-gfxclk`、`--max-vcn` 等旧频率参数在 Zen2 及以后**全部不支持**。在 Zen/Zen+ 上部分有效（如 `max-fclk-frequency` 在 Raven Ridge 支持，`--max-gfxclk` 在 Raven Ridge 上无效果但可尝试非 10 的倍数如 917 MHz）。

**Zen2+ 的替代方案：** 使用 `--gfx-clk`（通过 PSMU）可直接设置 iGPU 目标频率，Renoir ~ StrixHalo 均支持。

### 8. 电压调整有限

RyzenAdj 的常规参数无法直接调整电压。但有两条间接路径：

- **`--oc-volt`**：可设置核心 VID，但仅在 OC 模式下生效，且仅限 Lucienne/Renoir/Cezanne（**不含 Rembrandt**）。需按公式换算：`(1.55 - 目标电压) / 0.00625`
- **曲线优化器（`--set-coall` / `--set-coper`）**：负值相当于降压（降低功耗和温度，但可能不稳定），正值相当于加压。支持 Cezanne/Renoir/Lucienne 及之后所有系列

## 五、各代 CPU 支持情况

| 系列 | 架构 | 代表型号 | 支持状态 |
|------|------|----------|----------|
| Raven Ridge | Zen | Ryzen 2000 Mobile | ✅ 完全支持（含全部频率控制参数） |
| Picasso | Zen+ | Ryzen 3000 Mobile | ✅ 完全支持 |
| Dali | Zen | Athlon Mobile | ☑ 支持，未充分测试 |
| Renoir | Zen2 | Ryzen 4000 Mobile | ⚠ STAPM/stapm-time 无效；频率参数不支持；skin-temp 命令被接受但无实际效果；支持 `--gfx-clk`、`--oc-clk`/`--oc-volt`、CO（含 `--set-cogfx`） |
| Lucienne | Zen2 | Ryzen 5000 Mobile (低功耗) | ⚠ 同 Renoir；无 cap 机制 |
| Van Gogh | Zen2 | Steam Deck (Aerith) | ⚠ 同 Renoir + 独有 TDC/EDC GFX/CVIP 和 PSI3 参数；支持 `--gfx-clk` 和 `--set-cogfx` |
| Mendocino | Zen2 | Ryzen 7020 | ⚠ 同 Renoir，支持有限 |
| Cezanne | Zen3 | Ryzen 5000 Mobile | ⚠ 同 Renoir + 有厂商上限锁（cap） |
| Rembrandt | Zen3+ | Ryzen 6000 Mobile | ⚠ STAPM/stapm-time 无效；skin-temp 参数真正生效；有厂商上限锁（cap） |
| Dragon Range | Zen4 | Ryzen 7045 HX | ⚠ 同 Rembrandt；桌面级移动处理器 |
| Phoenix Point | Zen4 | Ryzen 7040 | ⚠ 同 Rembrandt |
| Hawk Point | Zen4 | Ryzen 8040/8045 | ⚠ 同 Phoenix |
| Strix Point | Zen5 | Ryzen AI 300 | ⚠ 同 Rembrandt；SMU 寄存器地址不同 |
| Krackan Point | Zen5 | Ryzen (入门级) | ⚠ 同 Strix Point；**不支持** `--set-cogfx`、`--apu-skin-temp`、`--dgpu-skin-temp` |
| Strix Halo | Zen5 | Ryzen (高端) | ⚠ STAPM/stapm-time 无效；`--apu-skin-temp` 和 `--dgpu-skin-temp` 不支持（仅 `--skin-temp-limit` 和 `--tctl-temp` 可用）；**不支持** `--set-cogfx`；SMU 寄存器地址不同 |
| Fire Range | Zen5 | Ryzen (桌面级移动) | ⚠ 同 Dragon Range；SMU 寄存器地址不同 |

## 六、常见错误与解决

| 错误信息 | 解决方案 |
|---------|---------|
| `pcilib: sysfs_write: write failed: Operation not permitted` | 关闭 Secure Boot |
| `WinRing0 Err: 0x2 Unable to get PCI Obj` | 以管理员身份运行；检查 Windows Defender → 设备安全 → 核心隔离 → 内存完整性 |
| `WinRing0 Err: Driver not loaded` | 以管理员身份运行；检查杀毒软件/反作弊是否阻止驱动加载 |
| `Unable to init ryzenadj, check permission` | 通用初始化错误，参考上述解决方案逐一排查 |
| `inpoutx64 got blocked by Anti Cheat` | 可安全删除 inpoutx64.dll（仅影响 `--info` 和 `--dump-table`，不影响参数调整）。使用 PowerShell 脚本时需禁用 `monitorField`，因为监控功能依赖 inpoutx64.dll |

## 七、完整参数速查表

| # | 参数 | 短选项 | 单位 | 类别 |
|---|------|--------|------|------|
| 1 | `--stapm-limit` | `-a` | mW | 功耗 |
| 2 | `--fast-limit` | `-b` | mW | 功耗 |
| 3 | `--slow-limit` | `-c` | mW | 功耗 |
| 4 | `--slow-time` | `-d` | 秒 | 功耗 |
| 5 | `--stapm-time` | `-e` | 秒 | 功耗 |
| 6 | `--tctl-temp` | `-f` | °C | 温度 |
| 7 | `--vrm-current` | `-g` | mA | 电流 |
| 8 | `--vrmsoc-current` | `-j` | mA | 电流 |
| 9 | `--vrmgfx-current` | — | mA | 电流 (Van Gogh) |
| 10 | `--vrmcvip-current` | — | mA | 电流 (Van Gogh) |
| 11 | `--vrmmax-current` | `-k` | mA | 电流 |
| 12 | `--vrmsocmax-current` | `-l` | mA | 电流 |
| 13 | `--vrmgfxmax-current` | — | mA | 电流 (Van Gogh) |
| 14 | `--psi0-current` | `-m` | mA | 电流 |
| 15 | `--psi3cpu-current` | — | mA | 电流 (Van Gogh) |
| 16 | `--psi0soc-current` | `-n` | mA | 电流 |
| 17 | `--psi3gfx-current` | — | mA | 电流 (Van Gogh) |
| 18 | `--max-socclk-frequency` | `-o` | MHz | 频率 |
| 19 | `--min-socclk-frequency` | `-p` | MHz | 频率 |
| 20 | `--max-fclk-frequency` | `-q` | MHz | 频率 |
| 21 | `--min-fclk-frequency` | `-r` | MHz | 频率 |
| 22 | `--max-vcn` | `-s` | MHz | 频率 |
| 23 | `--min-vcn` | `-t` | MHz | 频率 |
| 24 | `--max-lclk` | `-u` | MHz | 频率 |
| 25 | `--min-lclk` | `-v` | MHz | 频率 |
| 26 | `--max-gfxclk` | `-w` | MHz | 频率 |
| 27 | `--min-gfxclk` | `-x` | MHz | 频率 |
| 28 | `--prochot-deassertion-ramp` | `-y` | — | Prochot |
| 29 | `--apu-skin-temp` | — | °C | 温度 |
| 30 | `--dgpu-skin-temp` | — | °C | 温度 |
| 31 | `--apu-slow-limit` | — | mW | 功耗 |
| 32 | `--skin-temp-limit` | — | mW | 温度 |
| 33 | `--gfx-clk` | — | MHz | 频率 |
| 34 | `--oc-clk` | — | MHz | 超频 |
| 35 | `--oc-volt` | — | — | 超频 |
| 36 | `--enable-oc` | — | Flag | 超频 |
| 37 | `--disable-oc` | — | Flag | 超频 |
| 38 | `--set-coall` | — | — | 曲线优化器 |
| 39 | `--set-coper` | — | — | 曲线优化器 |
| 40 | `--set-cogfx` | — | — | 曲线优化器 |
| 41 | `--power-saving` | — | Flag | 电源模式 |
| 42 | `--max-performance` | — | Flag | 电源模式 |

## 八、参考资料

- [RyzenAdj GitHub 仓库](https://github.com/FlyGoat/RyzenAdj)
- [支持的型号列表](https://github.com/FlyGoat/RyzenAdj/wiki/Supported-Models)
- [Renoir 调优指南](https://github.com/FlyGoat/RyzenAdj/wiki/Renoir-Tuning-Guide)
- [完整参数选项](https://github.com/FlyGoat/RyzenAdj/wiki/Options)
- [常见问题解答](https://github.com/FlyGoat/RyzenAdj/wiki/FAQ)

相关文档：
- [构建与使用指南](build-and-usage.md) — 安装、编译、命令行用法、自动化配置
- [libryzenadj API 参考](libryzenadj-api.md) — C 库接口、PM Table 读取、集成示例
- [开发指南](development-guide.md) — 项目架构、SMU 协议、PM Table 偏移量、扩展指南
