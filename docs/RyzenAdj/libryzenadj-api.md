# libryzenadj API 参考

## 一、概述

libryzenadj 是 RyzenAdj 提供的 C 共享库，可用于在自己的程序中调用 RyzenAdj 的全部功能。库名：`libryzenadj.so`（Linux）/ `libryzenadj.dll`（Windows）。

当前版本：**v0.19.0**

**许可证：** LGPL

## 二、头文件引用

```c
#include "ryzenadj.h"
```

编译时链接：

```bash
# Linux
gcc -o myapp myapp.c -lryzenadj

# Windows (MSVC)
cl myapp.c /link libryzenadj.lib
```

## 三、生命周期管理

### init_ryzenadj

```c
ryzen_access init_ryzenadj();
```

初始化 RyzenAdj，返回不透明句柄。失败返回 `NULL`。

执行的操作：
1. 检测 CPU family（通过 CPUID）
2. 初始化 OS 访问对象（Linux: /dev/mem 或 ryzen_smu；Windows: WinRing0）
3. 连接 MP1 SMU 和 PSMU

**必须以 root/管理员权限调用。**

### cleanup_ryzenadj

```c
void cleanup_ryzenadj(ryzen_access ry);
```

释放所有资源。调用后 `ry` 不再有效。

> **线程安全**：`ryzen_access` 句柄**不是线程安全的**。多个线程不应同时操作同一个句柄。如需在多线程环境中使用，每个线程应创建独立的 `ryzen_access` 实例，或使用互斥锁保护共享实例。

### get_cpu_family

```c
enum ryzen_family get_cpu_family(ryzen_access ry);
```

返回 CPU 系列枚举值。可用于判断哪些参数在当前平台上可用。

### get_bios_if_ver

```c
int get_bios_if_ver(ryzen_access ry);
```

查询 SMU BIOS 接口版本号。该版本号反映了 SMU 固件的接口兼容性级别，不同版本可能支持不同的命令集。结果会被缓存，多次调用无额外开销。

## 四、错误码

所有 `set_*` 函数和 `init_table` / `refresh_table` 返回 `int`，0 表示成功，非零为错误码：

| 常量 | 值 | 含义 |
|------|----|------|
| `ADJ_ERR_FAM_UNSUPPORTED` | -1 | 当前 CPU 系列不支持此功能 |
| `ADJ_ERR_SMU_TIMEOUT` | -2 | SMU 响应超时 |
| `ADJ_ERR_SMU_UNSUPPORTED` | -3 | SMU 返回 UnknownCmd（固件不支持） |
| `ADJ_ERR_SMU_REJECTED` | -4 | SMU 拒绝请求（前置条件不满足/忙碌/失败） |
| `ADJ_ERR_MEMORY_ACCESS` | -5 | 内存访问错误 |

## 五、PM Table 操作

PM Table 是 SMU 维护的内存映射数据结构，包含实时功耗、温度、电流、频率等指标。布局随 table version 不同而变化。

### init_table

```c
int init_table(ryzen_access ry);
```

初始化 PM Table。必须在调用任何 `get_*` 读取函数之前调用。

执行的操作：
1. 向 PSMU 请求 table version 和 size
2. 获取物理地址并映射内存
3. 执行首次数据传输

**注意：** Raven/Picasso 首次启动后可能返回空数据，库内部有自动重试逻辑。

### refresh_table

```c
int refresh_table(ryzen_access ry);
```

刷新 PM Table 数据（重新从 SMU 传输到内存）。库内部会智能比较前 6 个 float 值避免冗余传输。

### get_table_ver

```c
uint32_t get_table_ver(ryzen_access ry);
```

返回 PM Table 版本标识符。不同版本的 table 布局不同。

### get_table_size

```c
size_t get_table_size(ryzen_access ry);
```

返回 table 大小（字节）。

### get_table_values

```c
float* get_table_values(ryzen_access ry);
```

返回指向 PM Table 内存副本的 `float*` 指针。可直接按偏移量访问原始数据（偏移量因 table version 而异）。

**指针有效期**：返回的指针在 `cleanup_ryzenadj()` 调用前始终有效。`refresh_table()` 会原地更新缓冲区内容（不会重新分配），因此无需在每次刷新后重新获取指针。

## 六、参数设置 API

### 返回值约定

所有 `set_*` 函数返回：
- `0` — 成功
- 负数 — 错误码（见上文）

**重要：** "成功"仅表示 SMU 接受了命令，不代表值一定生效。厂商可能对值进行截断（cap）。需通过 `get_*` 函数或 `--info` 验证实际值。

### 功耗限制（单位：mW）

```c
int set_stapm_limit(ryzen_access ry, uint32_t value);   // STAPM 持续功耗限制
int set_fast_limit(ryzen_access ry, uint32_t value);     // PPT FAST 瞬时功耗限制
int set_slow_limit(ryzen_access ry, uint32_t value);     // PPT SLOW 平均功耗限制
int set_apu_slow_limit(ryzen_access ry, uint32_t value); // APU PPT 慢速限制（A+dGPU 平台，Renoir ~ StrixHalo）
int set_skin_temp_power_limit(ryzen_access ry, uint32_t value); // 表面温度功耗限制（Renoir ~ StrixHalo，含 VanGogh/Mendocino）
```

### 时间常数（单位：秒）

```c
int set_slow_time(ryzen_access ry, uint32_t value);   // Slow PPT 时间常数
int set_stapm_time(ryzen_access ry, uint32_t value);  // STAPM 时间常数
```

### 温度限制（单位：°C）

```c
int set_tctl_temp(ryzen_access ry, uint32_t value);           // Tctl 温度目标（全系列）
int set_apu_skin_temp_limit(ryzen_access ry, uint32_t value); // APU 表面温度限制（STTv2, Renoir ~ HawkPoint）
int set_dgpu_skin_temp_limit(ryzen_access ry, uint32_t value);// 独显表面温度限制（STTv2, Renoir ~ StrixPoint）
```

> `apu_skin_temp` 和 `dgpu_skin_temp` 内部会将值乘以 256 后发送给 SMU。`tctl_temp` 则直接发送原始值（°C）。

### 电流限制（单位：mA）

```c
int set_vrm_current(ryzen_access ry, uint32_t value);       // TDC VDD
int set_vrmsoc_current(ryzen_access ry, uint32_t value);    // TDC SoC
int set_vrmgfx_current(ryzen_access ry, uint32_t value);    // TDC GFX（VanGogh 专用）
int set_vrmcvip_current(ryzen_access ry, uint32_t value);   // TDC CVIP（VanGogh 专用）
int set_vrmmax_current(ryzen_access ry, uint32_t value);    // EDC VDD（全系列，VanGogh 使用不同 SMU 命令）
int set_vrmsocmax_current(ryzen_access ry, uint32_t value); // EDC SoC（Raven ~ StrixHalo，**不含** DragonRange/FireRange）
int set_vrmgfxmax_current(ryzen_access ry, uint32_t value); // EDC GFX（VanGogh 专用）
int set_psi0_current(ryzen_access ry, uint32_t value);      // PSI0 VDD（Raven ~ Cezanne 含 Renoir/Lucienne）
int set_psi3cpu_current(ryzen_access ry, uint32_t value);   // PSI3 CPU（VanGogh 专用）
int set_psi0soc_current(ryzen_access ry, uint32_t value);   // PSI0 SoC（Raven ~ Cezanne 含 Renoir/Lucienne）
int set_psi3gfx_current(ryzen_access ry, uint32_t value);   // PSI3 GFX（VanGogh 专用）
```

### 频率控制（单位：MHz）

```c
int set_max_socclk_freq(ryzen_access ry, uint32_t value);   // SoC 时钟最大值
int set_min_socclk_freq(ryzen_access ry, uint32_t value);   // SoC 时钟最小值
int set_max_fclk_freq(ryzen_access ry, uint32_t value);     // CPU-GPU 传输频率最大值
int set_min_fclk_freq(ryzen_access ry, uint32_t value);     // CPU-GPU 传输频率最小值
int set_max_vcn(ryzen_access ry, uint32_t value);           // 视频编解码引擎最大频率
int set_min_vcn(ryzen_access ry, uint32_t value);           // 视频编解码引擎最小频率
int set_max_lclk(ryzen_access ry, uint32_t value);          // Data Launch Clock 最大值
int set_min_lclk(ryzen_access ry, uint32_t value);          // Data Launch Clock 最小值
int set_max_gfxclk_freq(ryzen_access ry, uint32_t value);   // iGPU 时钟最大值（Zen/Zen+ only, 含 Lucienne）
int set_min_gfxclk_freq(ryzen_access ry, uint32_t value);   // iGPU 时钟最小值（Zen/Zen+ only, 含 Lucienne）
int set_gfx_clk(ryzen_access ry, uint32_t value);           // 强制 iGPU 时钟（Renoir ~ StrixHalo, 通过 PSMU）
```

> 除 `set_gfx_clk`（Renoir ~ StrixHalo，含 VanGogh/Mendocino/KrackanPoint）外，频率控制参数仅在 Zen/Zen+ 上生效。

### Prochot

```c
int set_prochot_deassertion_ramp(ryzen_access ry, uint32_t value);
// Prochot 解除后的功率恢复斜坡时间（全系列，含 Mendocino）
```

### 超频（Renoir 及以上）

```c
int set_oc_clk(ryzen_access ry, uint32_t value);            // 强制核心时钟频率 (MHz)，Lucienne/Renoir/Cezanne/Rembrandt
int set_per_core_oc_clk(ryzen_access ry, uint32_t value);   // 逐核时钟频率（仅 API，无 CLI 参数），同上
int set_oc_volt(ryzen_access ry, uint32_t value);           // 强制核心 VID，Lucienne/Renoir/Cezanne（不含 Rembrandt）
int set_enable_oc(ryzen_access ry);                          // 启用 OC 模式，Lucienne/Renoir/Cezanne/Rembrandt
int set_disable_oc(ryzen_access ry);                         // 禁用 OC 模式，Lucienne/Renoir/Cezanne/Rembrandt
```

> `set_oc_volt` 的值需按公式计算：`(1.55 - 目标电压) / 0.00625`。Rembrandt 的 `set_enable_oc`/`set_disable_oc` 使用 PSMU 而非 MP1 SMU。

### 曲线优化器

```c
int set_coall(ryzen_access ry, uint32_t value);  // 全核曲线优化器（Renoir ~ StrixHalo, DragonRange/FireRange）
int set_coper(ryzen_access ry, uint32_t value);  // 逐核曲线优化器（同上）
int set_cogfx(ryzen_access ry, uint32_t value);  // iGPU 曲线优化器（Renoir ~ HawkPoint, 不含 KrackanPoint/StrixPoint/StrixHalo）
```

### 电源模式（无参数，布尔标志）

```c
int set_power_saving(ryzen_access ry);    // 省电模式
int set_max_performance(ryzen_access ry); // 性能模式
```

两者互斥。拔电源时系统自动设置 `power_saving`，插电源时自动设置 `max_performance`。

## 七、PM Table 读取 API

所有 `get_*` 函数返回 `float`。需先调用 `init_table()`，读取前建议调用 `refresh_table()` 获取最新数据。

### 功耗指标

```c
float get_stapm_limit(ryzen_access ry);       // STAPM 限制值
float get_stapm_value(ryzen_access ry);       // STAPM 当前值
float get_fast_limit(ryzen_access ry);        // PPT FAST 限制值
float get_fast_value(ryzen_access ry);        // PPT FAST 当前值
float get_slow_limit(ryzen_access ry);        // PPT SLOW 限制值
float get_slow_value(ryzen_access ry);        // PPT SLOW 当前值
float get_apu_slow_limit(ryzen_access ry);    // APU PPT SLOW 限制值
float get_apu_slow_value(ryzen_access ry);    // APU PPT SLOW 当前值
float get_socket_power(ryzen_access ry);      // 整体封装功耗
float get_soc_power(ryzen_access ry);         // SoC 功耗
```

### 电流指标

```c
float get_vrm_current(ryzen_access ry);          // TDC VDD 限制
float get_vrm_current_value(ryzen_access ry);    // TDC VDD 当前值
float get_vrmsoc_current(ryzen_access ry);       // TDC SoC 限制
float get_vrmsoc_current_value(ryzen_access ry); // TDC SoC 当前值
float get_vrmmax_current(ryzen_access ry);       // EDC VDD 限制
float get_vrmmax_current_value(ryzen_access ry); // EDC VDD 当前值
float get_vrmsocmax_current(ryzen_access ry);    // EDC SoC 限制
float get_vrmsocmax_current_value(ryzen_access ry); // EDC SoC 当前值
float get_psi0_current(ryzen_access ry);         // PSI0 VDD
float get_psi0soc_current(ryzen_access ry);      // PSI0 SoC
```

### 温度指标

```c
float get_tctl_temp(ryzen_access ry);             // Tctl 温度限制
float get_tctl_temp_value(ryzen_access ry);       // Tctl 当前温度
float get_apu_skin_temp_limit(ryzen_access ry);   // APU 表面温度限制
float get_apu_skin_temp_value(ryzen_access ry);   // APU 表面当前温度
float get_dgpu_skin_temp_limit(ryzen_access ry);  // dGPU 表面温度限制
float get_dgpu_skin_temp_value(ryzen_access ry);  // dGPU 表面当前温度
```

### 时间常数

```c
float get_stapm_time(ryzen_access ry);  // STAPM 时间常数
float get_slow_time(ryzen_access ry);   // Slow PPT 时间常数
```

### 时钟/性能状态

```c
float get_cclk_setpoint(ryzen_access ry);   // CCLK Boost 目标值
float get_cclk_busy_value(ryzen_access ry); // CCLK 繁碌值
```

### 逐核数据（core: 0-15）

```c
float get_core_clk(ryzen_access ry, uint32_t core);   // 核心时钟频率 (MHz)
float get_core_volt(ryzen_access ry, uint32_t core);  // 核心电压 (V)
float get_core_power(ryzen_access ry, uint32_t core);  // 核心功耗 (W)
float get_core_temp(ryzen_access ry, uint32_t core);   // 核心温度 (°C)
```

### L3 缓存

```c
float get_l3_clk(ryzen_access ry);    // L3 时钟频率
float get_l3_logic(ryzen_access ry);  // L3 逻辑功耗
float get_l3_vddm(ryzen_access ry);   // L3 VDDM 电压
float get_l3_temp(ryzen_access ry);   // L3 温度
```

### iGPU

```c
float get_gfx_clk(ryzen_access ry);   // iGPU 时钟频率
float get_gfx_temp(ryzen_access ry);  // iGPU 温度
float get_gfx_volt(ryzen_access ry);  // iGPU 电压
```

### 内存/Fabric

```c
float get_mem_clk(ryzen_access ry);  // 内存时钟频率
float get_fclk(ryzen_access ry);     // Fabric 时钟频率
```

### SoC

```c
float get_soc_volt(ryzen_access ry);  // SoC 电压
float get_soc_power(ryzen_access ry); // SoC 功耗
```

## 八、CPU Family 枚举

```c
enum ryzen_family {
    FAM_RAVEN = 0,       // Raven Ridge (Zen, Ryzen 2000 Mobile)
    FAM_PICASSO,         // Picasso (Zen+, Ryzen 3000 Mobile)
    FAM_RENOIR,          // Renoir (Zen2, Ryzen 4000 Mobile)
    FAM_CEZANNE,         // Cezanne (Zen3, Ryzen 5000 Mobile)
    FAM_DALI,            // Dali (Zen, Athlon Mobile)
    FAM_LUCIENNE,        // Lucienne (Zen2, Ryzen 5000 Mobile)
    FAM_VANGOGH,         // Van Gogh (Zen2, Steam Deck)
    FAM_REMBRANDT,       // Rembrandt (Zen3+, Ryzen 6000 Mobile)
    FAM_MENDOCINO,       // Mendocino (Zen2, Ryzen 7020)
    FAM_PHOENIX,         // Phoenix Point (Zen4, Ryzen 7040)
    FAM_HAWKPOINT,       // Hawk Point (Zen4, Ryzen 8040/8045)
    FAM_DRAGONRANGE,     // Dragon Range (Zen4, Ryzen 7045 HX)
    FAM_KRACKANPOINT,    // Krackan Point (Zen5)
    FAM_STRIXPOINT,      // Strix Point (Zen5, Ryzen AI 300)
    FAM_STRIXHALO,       // Strix Halo (Zen5)
    FAM_FIRERANGE,       // Fire Range (Zen5)
    FAM_END
};
```

## 九、集成示例

### C 语言完整示例

```c
#include <stdio.h>
#include "ryzenadj.h"

int main() {
    // 初始化
    ryzen_access ry = init_ryzenadj();
    if (!ry) {
        fprintf(stderr, "初始化失败，请以 root 权限运行\n");
        return 1;
    }

    printf("CPU Family: %d\n", get_cpu_family(ry));

    // 设置功耗限制
    int err;
    err = set_fast_limit(ry, 45000);
    if (err) fprintf(stderr, "set_fast_limit 失败: %d\n", err);

    err = set_slow_limit(ry, 35000);
    if (err) fprintf(stderr, "set_slow_limit 失败: %d\n", err);

    err = set_tctl_temp(ry, 85);
    if (err) fprintf(stderr, "set_tctl_temp 失败: %d\n", err);

    // 读取 PM Table 验证
    if (init_table(ry) == 0) {
        refresh_table(ry);
        printf("STAPM: %.1f / %.1f W\n", get_stapm_value(ry), get_stapm_limit(ry));
        printf("PPT FAST: %.1f / %.1f W\n", get_fast_value(ry), get_fast_limit(ry));
        printf("Tctl: %.1f / %.1f °C\n", get_tctl_temp_value(ry), get_tctl_temp(ry));
    }

    // 清理
    cleanup_ryzenadj(ry);
    return 0;
}
```

### STAPM Workaround（周期性重置 STAPM 使用量）

当 STAPM 被 cap 限制时，可通过重置 STAPM duration 来清除功耗历史，使 STAPM 限制永远不被触发：

```c
void reset_stapm_usage(ryzen_access ry) {
    set_stapm_limit(ry, get_stapm_limit(ry) - 5000);  // 降低 5W
    set_stapm_time(ry, 0);                              // duration 设为 0，清除历史
    // 等待 10ms（平台相关）
    set_stapm_time(ry, 500);                            // 恢复最大值
    set_stapm_limit(ry, get_stapm_limit(ry) + 5000);   // 恢复限制
}
```

## 十、SMU 通信架构

RyzenAdj 通过两个 SMU 实例（MP1 SMU 和 PSMU）与处理器通信。不同 CPU family 使用不同的寄存器地址和命令 ID。

> 详细的 SMU 协议、寄存器地址、响应码、命令 ID 映射，请参阅 [开发指南 — SMU 通信协议](development-guide.md#二smu-通信协议)。
>
> PM Table 版本与偏移量的完整映射，请参阅 [开发指南 — PM Table 详解](development-guide.md#三pm-table-详解)。

**要点**：
- DragonRange/FireRange 的 STAPM/fast/slow/tctl/vrm 等使用不同的 MP1 SMU 命令 ID（如 STAPM 用 `0x4f` 而非 `0x14`），CO 使用 PSMU 而非 MP1
- PM Table 前 6 个 float（offset 0x00-0x14）在所有版本中固定为：STAPM limit/value、PPT FAST limit/value、PPT SLOW limit/value
- 其余偏移量因 table version 而异，未知版本默认使用 0x1000 字节作为缓冲区大小

---

相关文档：
- [RyzenAdj 参数详解](ryzenadj-parameters.md) — 全部参数说明、各代 CPU 支持情况、常见陷阱
- [构建与使用指南](build-and-usage.md) — 安装、编译、命令行用法、自动化配置
- [开发指南](development-guide.md) — 项目架构、SMU 协议、PM Table 详解、扩展指南
