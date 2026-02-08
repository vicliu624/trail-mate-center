# Trail Mate Center

> 面向 Trail Mate / Meshtastic 设备的桌面控制中心

[English](README.md) | [中文](README_CN.md)

---

## 项目概览

Trail Mate Center 是 Trail Mate 或 Meshtastic 设备的上位机桌面应用，
通过 USB HostLink 连接设备，提供态势感知、消息流转、遥测可视化与协议分析能力。

本仓库仅包含桌面端应用与共享协议/核心库，**不包含设备端固件**。

---

## 核心功能

- USB HostLink 连接管理（串口扫描、自动重连、ACK 超时/重试）
- 实时聊天与会话列表、快速发送
- 态势面板：地图视图、节点列表、事件流
- 节点详情：遥测快照、最后在线状态
- 设备配置查看/编辑
- 日志、事件监视、原始帧检查
- 消息/事件导出（CSV/JSONL）
- 会话回放（Replay 文件）

---

## 快速开始

构建与运行（需要 .NET 8）：

```bash
dotnet run --project src/TrailMateCenter.App
```

---

## 项目范围

本仓库包含：

- 桌面端 UI（Avalonia）
- HostLink 协议实现
- Meshtastic protobuf 解码与数据模型
- 本地存储、导出与回放工具

本仓库不包含：

- 设备端固件
- 嵌入式 UI 或硬件驱动
- 移动端应用或商业服务

---

## 许可协议

本项目采用 **GNU Affero General Public License v3.0 (AGPLv3)** 授权。
详见 `LICENSE`。
