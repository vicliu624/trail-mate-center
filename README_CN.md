# Trail Mate Center

> 面向 Trail Mate / Meshtastic 野外节点的桌面控制中心

[English](README.md) | [中文](README_CN.md)

---

## 这是什么

Trail Mate Center 是一个桌面端上位机，用来通过 USB HostLink 连接 Trail Mate 或兼容 Meshtastic 的设备。
它把态势地图、消息收发、离线地图准备、协议排障、传播分析这些能力放在同一个 Avalonia 应用里。

这个仓库包含桌面应用和共享协议/核心库，**不包含设备固件**。

---

## 这个软件能做什么

### 连接与协议

- 扫描串口并通过 USB HostLink 连接设备
- 配置 ACK 超时与重试次数
- 支持自动连接、自动重连
- 支持回放模式，用录制文件离线复盘
- 同时兼容 Meshtastic 风格流量和 Trail Mate MeshCore 协议解码

### 态势面板

- 在地图上查看实时位置，支持 `OSM`、`Terrain`、`Satellite` 三种底图
- 叠加等高线和 NASA GIBS 影像图层
- 在同一页面查看队伍、节点列表、事件流和战术指令区
- 支持跟随最新位置，也支持手动拖图排查
- 支持快捷广播、`Rally / Move / Hold` 指令操作
- 提供离线模式，可关闭 APRS-IS 和 MQTT 外部源，只看本地与缓存数据

### 离线地图缓存与导出

- 直接在地图上框选或多边形圈选缓存区域
- 保存常用缓存区域，后续可继续构建或复查完整度
- 按所选底图和等高线图层构建离线瓦片
- 对不完整区域继续补缓存
- 将缓存区域导出到 U 盘 / SD 卡，目录结构为 `maps/base/...` 与 `maps/contour/...`
- 导入 `KML` 轨迹，并按路线缓冲区一键准备地图缓存
- 聊天中的位置预览会优先复用本地缓存瓦片

### 聊天与消息

- 查看会话列表和消息历史
- 在聊天页或快捷输入区给目标节点发消息
- 查看所选节点的在线状态、遥测摘要和最后位置
- 对发送失败的消息执行重试

### 配置与外部集成

- 读取和编辑设备配置项
- 调整战术阈值和事件严重级别规则
- 配置 APRS-IS 网关参数
- 配置一个或多个 Meshtastic MQTT 数据源
- 配置等高线生成，并测试 Earthdata Token 是否可用
- 在顶部栏切换语言和主题

### 日志、排障与数据导出

- 按级别过滤日志，并支持保存或复制
- 查看持久化事件流
- 检查原始帧，做协议级排障
- 将消息或事件导出为 `CSV` / `JSONL`
- 本地持久化会话，后续可以回放复盘

### 传播分析工作台

- 支持覆盖、干扰、中继优化、高级分析、校准等模式
- 可直接在工作台里编辑场景站点
- 可调整无线参数、植被/杂波、随机性与优化参数
- 可查看分析图层、图例、悬停探针和路径证据
- 仿真完成后可导出结果
- 可选接入 Unity 视口 / Bridge，用于更丰富的地形展示与诊断

---

## 主要标签页

| 标签页 | 用途 |
| --- | --- |
| `态势` | 实时地图、队伍视图、节点列表、事件流、战术指令、离线地图工具 |
| `传播` | 覆盖分析、场景编辑、分析图层、结果导出 |
| `聊天` | 会话列表、消息历史、快捷发送、节点上下文 |
| `配置` | 设备配置、战术阈值、APRS、MQTT、等高线、语言、主题 |
| `日志` | 应用日志过滤、复制与保存 |
| `事件监视` | 持久化战术事件流 |
| `原始帧` | 底层帧检查与协议排障 |
| `导出` | 将消息 / 事件导出为 `CSV` 或 `JSONL` |

---

## 快速开始

### 环境要求

- `.NET 8 SDK`

### 启动

```bash
dotnet run --project src/TrailMateCenter.App
```

### 首次使用建议

1. 在顶部栏选择设备串口
2. 视链路情况调整 ACK 超时和重试次数
3. 点击 `连接`
4. 打开 `态势` 页面确认地图、节点和事件是否开始更新
5. 如果要接 APRS、MQTT 或等高线功能，再进入 `配置` 页面继续设置

如果你现在不是连真机，而是想看录制数据，可以先勾选 `Replay` 并指定回放文件，再发起连接。

---

## 离线地图推荐流程

1. 在 `态势` 页面用地图框选工具选择区域，或者先导入 `KML` 轨迹
2. 选择要缓存的底图和等高线层
3. 启动缓存任务并等待状态完成
4. 打开“缓存区域列表”检查完整度，不足时继续补缓存
5. 确认区域足够完整后，再导出到 U 盘或 SD 卡

说明：

- 底图缓存默认放在 `Documents/TrailMateCenter`
- 等高线生成依赖有效的 Earthdata Token
- 导出状态现在会额外提示“源缓存是否仍有缺瓦片”，方便在出发前发现问题

---

## 本地数据默认位置

Windows 下常见默认路径：

- 数据库：`%LocalAppData%\TrailMateCenter\trailmate.db`
- 设置文件：`%AppData%\TrailMateCenter\settings.json`
- 地图缓存：`%UserProfile%\Documents\TrailMateCenter\`

地图缓存主要子目录：

- `tilecache`：OSM 底图
- `terrain-cache`：地形底图
- `satellite-cache`：卫星底图
- `contours/tiles`：生成后的等高线瓦片

---

## 仓库范围

本仓库包含：

- Avalonia 桌面界面
- HostLink 协议实现
- Meshtastic protobuf 解码与共享数据模型
- 本地存储、回放、导出、离线地图缓存工具
- 传播引擎、契约层，以及配套 bridge / service 代码

本仓库不包含：

- 设备固件
- 运行在设备端的嵌入式 UI
- 移动端应用
- 托管式云服务

---

## 相关文档

- [离线地图缓存对接说明](docs/TRAIL_MATE_CACHE_INTEGRATION.md)
- [传播分析实现文档](docs/propagation/implementation/README.md)
- [传播分析可解释性文档](docs/propagation/explainability/README.md)
- [传播分析知识笔记](docs/propagation/knowledge/README.md)

---

## 许可证

本项目采用 **GNU Affero General Public License v3.0 (AGPLv3)** 授权。
详见 [LICENSE](LICENSE)。
