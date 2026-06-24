# RyzenAdj 参数详解

## 一、RyzenAdj 是什么

RyzenAdj 是一个针对 **AMD Ryzen 移动平台**（笔记本）的功率调节工具，通过写入 SMU（System Management Unit）来实时调整 CPU 的功耗、温度、电流等限制参数。**不支持桌面平台**（桌面平台请用 BIOS 或 Ryzen Master）。

设置**重启后不保留**，每次开机恢复出厂值。电源模式切换（拔插电源线等）也会重置参数。

## 二、全部 31 个参数的作用

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
| `--stapm-limit` | mW | **STAPM 持续功耗限制**（第3级）。**Zen2/Zen3 上被 STTv2 覆盖，通常无效** |
| `--stapm-time` | 秒 | STAPM 时间常数。**Renoir 上无效** |
| `--apu-slow-limit` | mW | APU PPT 慢速限制（用于有独显的 A+A 平台）。**Renoir 上可能无效** |

**层级关系：Fast Limit > Slow Limit > STAPM Limit**

### 温度限制类

| 参数 | 单位 | 作用 |
|------|------|------|
| `--tctl-temp` | °C | **Tctl 温度目标**。建议比实际 prochot 低 5-10°C，否则触发 prochot 后 CPU 功耗骤降至 4W |
| `--apu-skin-temp` | °C | **APU 表面温度限制**（STTv2）。控制设备外壳温度，Zen2/Zen3 独有 |
| `--dgpu-skin-temp` | °C | **独显表面温度限制**（STTv2）。仅对有独显的设备有用 |
| `--skin-temp-limit` | mW | **表面温度触发后的功耗限制值**。过温时应用到 STAPM，需低于 `--slow-limit` |

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
| `--prochot-deassertion-ramp` | Prochot 解除后的功率恢复斜坡时间。值越大，prochot 后恢复越慢、限制越严格 |
| `--power-saving` | 省电模式（Flag）。限制 boost 延迟约 10 秒，降低空闲功耗。**拔电源时自动设置** |
| `--max-performance` | 性能模式（Flag）。消除 boost 延迟，快速达到最大频率。**插电源时自动设置** |

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
- `--stapm-limit`：**仅 Zen/Zen+ 有效**，Zen2/Zen3 被 STTv2 覆盖

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

### 4. Zen3 有厂商锁定

Cezanne/Rembrandt 的厂商可设置上限锁，导致调整值被 cap。`--stapm-limit` 可能无法提高。workaround：定期重置 STAPM 使用量（`readjustService.ps1` 中的 `$resetSTAPMUsage = $true`）。

### 5. STTv2 在 Zen2/Zen3 上是关键

STTv2（皮肤温度保护）会覆盖 `--stapm-limit`，并用 `--fast-limit` 或 `--skin-temp-limit` 来限功耗。如果调了 STAPM 没效果，大概率是 STTv2 在起作用。

### 6. 功耗限制是分层的

实际功耗受多层限制约束：温度、TDC、EDC、PPT FAST、PPT SLOW、STAPM、皮肤温度——**任何一层都可能成为瓶颈**。需要用 `--info` 在满载时检查具体是哪个限制在起作用。

### 7. 频率控制参数基本废了

`--max-gfxclk`、`--max-vcn` 等频率参数在 Zen2 及以后**全部不生效**，仅对 Zen/Zen+ 有意义。

### 8. 不支持降压

RyzenAdj 无法调整电压，没有 undervolting 功能。

## 五、各代 CPU 支持情况

| 系列 | 架构 | 支持状态 |
|------|------|----------|
| Raven Ridge | Zen | ✅ 完全支持 |
| Picasso | Zen+ | ✅ 完全支持 |
| Dali | Zen | ☑ 支持，未充分测试 |
| Renoir | Zen2 | ⚠ 部分参数无效（STAPM 受 STTv2 影响） |
| Lucienne | Zen2 | ⚠ 部分参数无效 |
| Van Gogh | Zen2 | Steam Deck 等设备 |
| Cezanne | Zen3 | ⚠ 部分参数无效，有厂商上限锁 |
| Rembrandt | Zen3+ | ⚠ 部分参数无效，有厂商上限锁 |

## 六、常见错误与解决

| 错误信息 | 解决方案 |
|---------|---------|
| `pcilib: sysfs_write: write failed: Operation not permitted` | 关闭 Secure Boot |
| `WinRing0 Err: 0x2 Unable to get PCI Obj` | 以管理员身份运行；检查 Windows Defender → 设备安全 → 核心隔离 → 内存完整性 |
| `WinRing0 Err: Driver not loaded` | 以管理员身份运行；检查杀毒软件/反作弊是否阻止驱动加载 |
| `Unable to init ryzenadj, check permission` | 通用初始化错误，参考上述解决方案逐一排查 |
| `inpoutx64 got blocked by Anti Cheat` | 可安全删除 inpoutx64.dll（仅影响 `--info` 和 `--dump-table`，不影响参数调整） |

## 七、参考资料

- [RyzenAdj GitHub 仓库](https://github.com/FlyGoat/RyzenAdj)
- [支持的型号列表](https://github.com/FlyGoat/RyzenAdj/wiki/Supported-Models)
- [Renoir 调优指南](https://github.com/FlyGoat/RyzenAdj/wiki/Renoir-Tuning-Guide)
- [完整参数选项](https://github.com/FlyGoat/RyzenAdj/wiki/Options)
- [常见问题解答](https://github.com/FlyGoat/RyzenAdj/wiki/FAQ)
