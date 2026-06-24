# RyzenTunerNext

AMD Ryzen 移动平台功率调优工具的文档与参考项目。

基于 [RyzenAdj](https://github.com/FlyGoat/RyzenAdj) 的参数研究和中文文档，旨在为中文用户提供准确、详尽的 RyzenAdj 参数参考和使用指南。

## 项目定位

RyzenAdj 是一个通过 SMU（System Management Unit）实时调整 AMD Ryzen 移动平台 CPU 功耗、温度、电流等限制参数的命令行工具。**不支持桌面平台**。

本项目为 RyzenAdj 提供：
- 详细的中文参数文档（含各代 CPU 实际生效情况）
- libryzenadj 开发者 API 参考
- 使用场景指南与最佳实践

## 文档目录

| 文档 | 内容 |
|------|------|
| [RyzenAdj 参数详解](ryzenadj-parameters.md) | 全部 42 个设置参数的作用、单位、各代 CPU 支持情况、核心参数优先级、常见陷阱 |
| [libryzenadj API 参考](libryzenadj-api.md) | C 库 API 接口、生命周期管理、PM Table 读取、错误码、集成示例 |
| [构建与使用指南](build-and-usage.md) | Linux/Windows 构建方法、依赖安装、命令行用法、自动化服务配置 |
| [开发指南](development-guide.md) | 项目架构、SMU 通信协议、PM Table 详解、平台后端、扩展指南 |

## 快速开始

```bash
# 查看当前功耗状态（需要 root/管理员权限）
sudo ryzenadj --info

# 设置功耗限制示例：快通道 45W、慢通道 35W、温度上限 85°C
sudo ryzenadj --fast-limit=45000 --slow-limit=35000 --tctl-temp=85

# 验证设置是否生效
sudo ryzenadj --info
```

> **重要**：设置**重启后不保留**，需通过脚本或服务定期重新应用。

## 支持的 CPU 系列

| 系列 | 架构 | 代表型号 |
|------|------|----------|
| Raven Ridge | Zen | Ryzen 2000 Mobile |
| Picasso | Zen+ | Ryzen 3000 Mobile |
| Dali | Zen | Athlon Mobile |
| Renoir | Zen2 | Ryzen 4000 Mobile |
| Lucienne | Zen2 | Ryzen 5000 Mobile (低功耗) |
| Van Gogh | Zen2 | Steam Deck |
| Mendocino | Zen2 | Ryzen 7020 |
| Cezanne | Zen3 | Ryzen 5000 Mobile |
| Rembrandt | Zen3+ | Ryzen 6000 Mobile |
| Dragon Range | Zen4 | Ryzen 7045 HX |
| Phoenix Point | Zen4 | Ryzen 7040 |
| Hawk Point | Zen4 | Ryzen 8040/8045 |
| Strix Point | Zen5 | Ryzen AI 300 |
| Krackan Point | Zen5 | Ryzen (入门级) |
| Strix Halo | Zen5 | Ryzen (高端) |
| Fire Range | Zen5 | Ryzen (桌面级移动) |

## 许可证

本文档项目基于 RyzenAdj（LGPL 许可证）的公开信息编写。RyzenAdj 由 Jiaxun Yang 开发。
