# RyzenAdj 参数详解

## 一、RyzenAdj 是什么

RyzenAdj 是一个针对 **AMD Ryzen 移动平台**（笔记本）的功率调节工具，通过写入 SMU（System Management Unit）来实时调整 CPU 的功耗、温度、电流等限制参数。**不支持桌面平台**（桌面平台请用 BIOS 或 Ryzen Master）。

设置**重启后不保留**，每次开机恢复出厂值。电源模式切换（拔插电源线等）也会重置参数。

### 术语说明

- **cap**（封顶/上限锁定）：Zen3 起，厂商可对 PM Table 参数设置最大允许值。当你设置的值超过上限时，命令返回"成功"，但实际值被截断到厂商设定的上限。例如厂商将 `slow-limit` 上限设为 35W，你执行 `--slow-limit=40000`（40W），实际生效值为 35.001W。

## 二、全部 29 个设置参数的作用

### 调试/信息类（2个）

| 参数 | 作用 |
|------|------|
| `--info` | 显示 CPU 型号及关键功耗指标（调整前后均可查看） |
| `--dump-table` | 显示完整 PM Table，用于排查设置是否被正确应用或被覆盖 |

### 功耗限制类（核心）

| 参数 | 单位 | 作用 |
|------|------|------|
| `--fast-limit` | mW | **PPT FAST 限制** — 瞬时/峰值功耗上限。在 `--slow-time` 时间窗口内的短时功耗天花板 |
| `--slow-limit` | mW | **PPT SLOW 限制** — 平均功耗上限。长期持续功耗的天花板 |
| `--slow-time` | 秒 | 慢 PPT 时间常数，控制平均功耗的计算窗口。**值越大，boost 维持时间越长** |
| `--stapm-limit` | mW | **STAPM 持续功耗限制**（第3级）。**Zen2/Zen3 上默认被 STTv2 覆盖（STTv2 启用时无效）**；仅在 STTv2 被禁用时可用 |
| `--stapm-time` | 秒 | STAPM 时间常数。**Renoir 上值甚至不会写入 PM Table**；STTv2 启用时也不会改变值 |
| `--apu-slow-limit` | mW | APU PPT 慢速限制（用于有独显的 A+A 平台）。**Renoir 上命令被接受、值可改变，但 usage power 始终报告为 0，通常无实际效果**；仅在无 apu-skin-temp 和 stapm/fast/slow/skin 限制时才可能生效 |

**层级关系：Fast Limit > Slow Limit > STAPM Limit**

### 温度限制类

| 参数 | 单位 | 作用 |
|------|------|------|
| `--tctl-temp` | °C | **Tctl 温度目标**。建议比实际 prochot 低 5-10°C，否则触发 prochot 后 CPU 功耗骤降至 4W |
| `--apu-skin-temp` | °C | **APU 表面温度限制**（STTv2）。控制设备外壳温度。**仅 Rembrandt (Zen3+) 支持**；Renoir/Cezanne 上命令被接受但无实际效果 |
| `--dgpu-skin-temp` | °C | **独显表面温度限制**（STTv2）。仅对有独显的设备有用。**仅 Rembrandt (Zen3+) 支持**；Renoir/Cezanne 上无实际效果 |
| `--skin-temp-limit` | mW | **表面温度触发后的功耗限制值**。过温时应用到 STAPM，需低于 `--slow-limit`。**Renoir/Cezanne 上无实际效果**，仅 Rembrandt (Zen3+) 支持 |

### 电流限制类

| 参数 | 单位 | 作用 |
|------|------|------|
| `--vrm-current` | mA | **TDC VDD** — 长时间 VRM 电流限制。仅在 PM Table 显示为瓶颈时调整 |
| `--vrmmax-current` | mA | **EDC VDD** — 峰值 VRM 电流限制。**对 GPU 性能影响更大** |
| `--vrmsoc-current` | mA | **TDC SoC** — SoC 长时间电流限制。SoC 很少成为瓶颈 |
| `--vrmsocmax-current` | mA | **EDC SoC** — SoC 峰值电流限制 |
| `--psi0-current` | mA | PSI0 VDD 电流限制。**功能不明确，部分设备无效果** |
| `--psi0soc-current` | mA | PSI0 SoC 电流限制。同上 |

### 频率控制类（Zen/Zen+ 为主）

| 参数 | 单位 | 作用 |
|------|------|------|
| `--max-socclk-frequency` / `--min-socclk-frequency` | MHz | SoC 时钟频率范围 |
| `--max-fclk-frequency` / `--min-fclk-frequency` | MHz | CPU-GPU 传输频率范围 |
| `--max-vcn` / `--min-vcn` | MHz | Video Core Next（视频编解码引擎）频率范围 |
| `--max-lclk` / `--min-lclk` | MHz | Data Launch Clock 频率范围 |
| `--max-gfxclk` / `--min-gfxclk` | MHz | GFX（集成显卡）时钟频率范围 |

> **Zen2/Zen3 上全部不生效。**

### Prochot 与模式切换

| 参数 | 作用 |
|------|------|
| `--prochot-deassertion-ramp` | Prochot 解除后的功率恢复斜坡时间。值越大，prochot 后恢复越慢、限制越严格。实测参考：值 < 256 基本无效果；值 ~300 时 prochot 后功耗约 15W；值 ~20000 时 prochot 后功耗约 6W；限制至少持续 10 分钟以上 |
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

- `--apu-skin-temp`：设备外壳过热时调
- `--vrm-current`：PM Table 显示 TDC 为瓶颈时调
- `--max-performance` / `--power-saving`：电池/AC 模式切换场景
- `--stapm-limit`：**仅 Zen/Zen+ 有效**，Zen2/Zen3 默认被 STTv2 覆盖（STTv2 禁用时可用）

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

STTv2 仅存在于 Zen2/Zen3/Zen3+。skin-temp 相关参数（`--apu-skin-temp`、`--dgpu-skin-temp`、`--skin-temp-limit`）在 Renoir/Cezanne 上命令被接受但无实际效果，仅 Rembrandt (Zen3+) 真正支持。

### 6. 功耗限制是分层的

实际功耗受多层限制约束：温度、TDC、EDC、PPT FAST、PPT SLOW、STAPM、皮肤温度——**任何一层都可能成为瓶颈**。需要用 `--info` 在满载时检查具体是哪个限制在起作用。

### 7. 频率控制参数基本废了

`--max-gfxclk`、`--max-vcn` 等频率参数在 Zen2 及以后**全部不支持**。在 Zen/Zen+ 上部分有效（如 `max-fclk-frequency` 在 Raven Ridge 支持，`max-gfxclk` 在 Raven Ridge 上无效果但可尝试非 10 的倍数如 917 MHz）。

### 8. 不支持降压

RyzenAdj 无法调整电压，没有 undervolting 功能。

## 五、各代 CPU 支持情况

| 系列 | 架构 | 支持状态 |
|------|------|----------|
| Raven Ridge | Zen | ✅ 完全支持 |
| Picasso | Zen+ | ✅ 完全支持 |
| Dali | Zen | ☑ 支持，未充分测试 |
| Renoir | Zen2 | ⚠ STAPM/stapm-time/skin-temp 无效；频率参数不支持 |
| Lucienne | Zen2 | ⚠ 同 Renoir；无 Zen3 cap 机制 |
| Van Gogh | Zen2 | Steam Deck (Aerith)；同 Renoir 支持状态 |
| Cezanne | Zen3 | ⚠ 同 Renoir + 有厂商上限锁（cap） |
| Rembrandt | Zen3+ | ⚠ STAPM/stapm-time 无效；skin-temp 参数支持；有厂商上限锁（cap） |

## 六、常见错误与解决

| 错误信息 | 解决方案 |
|---------|---------|
| `pcilib: sysfs_write: write failed: Operation not permitted` | 关闭 Secure Boot |
| `WinRing0 Err: 0x2 Unable to get PCI Obj` | 以管理员身份运行；检查 Windows Defender → 设备安全 → 核心隔离 → 内存完整性 |
| `WinRing0 Err: Driver not loaded` | 以管理员身份运行；检查杀毒软件/反作弊是否阻止驱动加载 |
| `Unable to init ryzenadj, check permission` | 通用初始化错误，参考上述解决方案逐一排查 |
| `inpoutx64 got blocked by Anti Cheat` | 可安全删除 inpoutx64.dll（仅影响 `--info` 和 `--dump-table`，不影响参数调整）。使用 PowerShell 脚本时需禁用 `monitorField`，因为监控功能依赖 inpoutx64.dll |

## 七、参考资料

- [RyzenAdj GitHub 仓库](https://github.com/FlyGoat/RyzenAdj)
- [支持的型号列表](https://github.com/FlyGoat/RyzenAdj/wiki/Supported-Models)
- [Renoir 调优指南](https://github.com/FlyGoat/RyzenAdj/wiki/Renoir-Tuning-Guide)
- [完整参数选项](https://github.com/FlyGoat/RyzenAdj/wiki/Options)
- [常见问题解答](https://github.com/FlyGoat/RyzenAdj/wiki/FAQ)
