# APRS 全实现与 USB HostLink 扩展规范

版本: 0.1  
日期: 2026-02-07

本规范用于 TrailMate Center 的 APRS“全实现”与 RF→IS iGate 行为定义，并明确设备端 USB HostLink 协议需要补充的能力与帧格式。规范文字中的“必须/应/可”分别对应强制/推荐/可选要求。

## 1. 目标与范围

- 目标: 完整覆盖 APRS 主要语义与常见扩展，保证 RF→IS 注入正确、无污染、可观测、可追溯。
- 范围:
  - APRS 解析与重编码。
  - iGate RF→IS 注入语义与防护机制。
  - 本地状态存储与可观测性。
  - 设备端 USB HostLink 协议扩展(新增能力、命令与事件)。
  - 输入源为 Meshtastic 业务数据(位置/遥测/文本/状态等)，主机侧生成 APRS-IS 文本行。
- 非目标:
  - 不强制 IS→RF 转发(可扩展)。
  - 不要求 APRS-IS 登录/认证细节实现于设备端(由主机侧完成)。
  - 本期不做 RF 侧发射(TX)，相关内容仅作为可选扩展。

## 2. 核心数据模型

实现中必须存在以下结构化语义字段(不只是字符串拼接):

- Address:
  - Callsign + SSID(0..15)
  - Destination(TOCALL)
  - Source/Third-party(支持 3rd-party encapsulated traffic，前缀 `}` )
- Path:
  - 解析为有序列表，每个 hop 具备: 原始 token、是否已用(`*`)、类型(显式 callsign-ssid / alias / q-construct / TCPIP*)
- Position:
  - 坐标、符号表/符号、压缩/非压缩、扩展字段(速度/航向/高度/范围/PHG/RNG/DAO)
  - 时间戳与更新时间
- Message:
  - addressee(固定 9 字段)
  - 文本/ID/ACK/REJ
- Object/Item:
  - 对象名、存活/撤销标志、位置、时间戳、注释
- Telemetry:
  - T# 序号、A1..A5、D1..D8、元定义(PARM/UNIT/EQNS/BITS)
- Weather:
  - 风向/风速/阵风/温度/雨量/气压/湿度等字段
- iGate 运行态:
  - 去重缓存、限速计数、队列、last-heard、last-pos、object/telemetry 状态

## 3. 身份与地址语义

1) Callsign + SSID
- 必须支持 `CALL-SSID`，SSID 范围 0..15。
- 必须区分无 SSID 与 SSID=0 的格式差异(编码时保持一致性)。

2) Destination(TOCALL)
- 必须保留并解析 TOCALL 以识别软件/设备类型(例如 `APRS`, `APZxxx` 等)。
- 应将 TOCALL 作为语义字段写入内部模型，避免仅作字符串输出。

3) Third-party 封装
- 必须支持 `}` 前缀封装帧(3rd-party traffic)。
- 必须解析内层包作为独立 APRS 数据包处理(含路径、时间戳、对象等)。

4) Object/Item 命名规则
- Object 名称固定 9 字符宽度，空格填充/裁剪。
- Item 名称长度 3..9 字符。
- 必须保持命名规则用于去重与状态更新。

## 4. Path 与 iGate 注入语义

1) Path 解析
- 必须完整解析 path 列表，支持:
  - 明确 callsign-ssid
  - WIDEn-n / TRACE* / ALIAS
  - TCPIP* / TCPXX / q 架构
- 必须识别 `*` 已用标志，例如 `WIDE1*`。

2) digipeater hops
- 必须正确处理 `WIDE1-1`, `WIDE2-2`, `TRACE*` 等语义。
- 必须能区分“已用”和“未用”，用于 RF→IS 的路径重写逻辑。

3) RF→IS 重写规则(强制)
- RF 注入 IS 时，必须移除/终结 RF 扩散路径(不得携带 `WIDE*` 扩散语义进入 IS)。
- 必须插入正确的 q 架构字段:
  - 直收: `qAR,<IGATE_CALL>`
  - 非直收: `qAO,<IGATE_CALL>`
  - 如需支持其它角色，可扩展 `qAS/qAC`。
- 必须插入 iGate 自身 callsign(例如 `qAR,BG6XXX-10`)。

4) 直收/非直收判定逻辑
- 必须基于 RF 接收元数据(是否直听/是否经由本机 digi/是否 heard-via)决定 qAR vs qAO。
- 判定逻辑必须可配置与可观测。

5) Loop/污染防护
- 必须阻止把来自 APRS-IS 的包再次注回 IS。
- 必须对 3rd-party encapsulated traffic 做环路识别(内层包的来源与路径)。

## 5. 位置(Position)语义

### A. 非压缩位置(Uncompressed)
- 支持 `/` 或 `\` 符号表
- 格式: `DDMM.mmN/DDDMM.mmE`
- 可带扩展:
  - Course/Speed(`ccc/sss`)
  - PHG
  - RNG
  - Altitude(`/A=xxxxxx`)
  - DAO(高精度扩展，必须支持解析与输出)

### B. 压缩位置(Compressed)
- 支持 Base91 编码的经纬度/符号
- 必须正确处理:
  - 符号表/符号 code
  - 扩展类型标志位
  - course/speed 或 altitude 或 range 的互斥语义

### C. Mic-E
- 必须支持 Mic-E 编码(目的地址字段携带位置/速度/航向)。
- 必须支持 Mic-E 状态/消息位解析。
- RF→IS 输出仍为合法 APRS 包(原样或按 Mic-E 规则重编码)。

### D. 时间戳(Timestamp)
- 支持 `z`(UTC)、`/`(本地)等格式。
- 位置包/对象包中的时间含义必须保留。
- 系统需维护位置 freshness 与过期策略:
  - 必须维护 last-update。
  - 必须按类型设置去抖与过期阈值(用于限速/状态判断)。

## 6. 符号系统(Symbol)

- 必须支持 Symbol Table `/` 与 `\`。
- Symbol Code 为单字符，必须解析与保留。
- 支持 overlay(表 `\` 下的 overlay 语义)。
- 系统内部应将符号映射为“对象/单位类型”语义字段，而不是纯装饰。

## 7. 消息(Message)语义

- `:` 消息帧解析必须实现:
  - addressee 固定 9 字符字段
  - 文本消息与 Message ID
- ACK/REJ:
  - 必须解析并正确转发 ACK/REJ
  - 即使仅做 RF→IS 也必须保留语义
- Bulletin / Announcement:
  - 支持 `BLN` 开头的 addressee 变体
- 去重:
  - 以 (src, dst, msgid, 内容, 时间窗) 作为去重键
  - 防止 LoRa 侧重发造成 IS 噪声

## 8. Object / Item

- Object:
  - 格式: `;` + 9 字符对象名 + alive/killed 标志 + 时间戳 + 位置 + 注释
  - 必须支持 killed 语义(对象撤销)
- Item:
  - 格式: `)` + 3..9 字符对象名 + 位置 + 注释
- 必须维护对象生命周期与最后状态(否则无法称为全实现)

## 9. 状态(Status)

- `>` 状态帧必须解析与承载。
- 可能包含频率/音调/范围等约定字段:
  - 不要求强解释，但必须完整保留，不得破坏格式。
- 状态与位置的关系必须允许并存(意图/模式描述)。

## 10. Telemetry(遥测)全实现

- `T#` 序号帧必须支持。
- 5 路模拟 A1..A5，8 路数字 D1..D8 必须解析。
- 元定义帧必须支持:
  - `PARM.`, `UNIT.`, `EQNS.`, `BITS.`
- 系统必须将 telemetry 转为可查询状态(不仅是转发)。

## 11. Weather(气象)语义

- 必须识别气象报告组合字段:
  - 风向/风速/阵风/温度/雨量/气压/湿度等
- 常见气象格式可能出现在位置注释或专用帧中:
  - 必须识别为 Weather 类型并完整保留字段与原始格式。

## 12. Queries / Responses

- `?` 查询包必须解析并可转发:
  - 常见 `?APRSP`, `?WX`, `?IGATE` 等
- 作为 RF→IS iGate:
  - 至少要正确解析并转发
  - 可选支持本地响应后再上报

## 13. 其他 APRS 数据类型

建议纳入“全实现覆盖”:
- DX Spotting
- RFID / Objects 扩展
- Emergency / Priority(与符号、注释结合的求救约定)
- 3rd-party encapsulated traffic(关键)

## 14. iGate 工程机制(全实现必要条件)

即使仅做 RF→IS，也必须实现以下机制:

1) 去重/抑制
- 位置/消息/对象/遥测各自策略
- 必须记录去重命中与丢弃原因

2) 限速
- 每 callsign + 每类型的最大发送频率
- 必须可配置

3) 队列与重连
- APRS-IS 断线缓存
- 重连后有序发送
- 必须丢弃过期数据

4) 一致性存储
- last-heard
- last-pos
- object 状态
- telemetry 状态

5) 可观测性
- 统计: 注入条数、丢弃原因、去重命中、错误码
- 可追踪某条包的决策链路(解析/去重/限速/注入)

## 15. USB HostLink 设备端调整(协议扩展)

当前 HostLink 支持基础命令/事件与 AppData 传输，设备侧通过 Meshtastic 协议携带业务数据(位置/遥测/文本等)。为实现上述功能，HostLink 需要把 Meshtastic 业务数据与接收元数据送到主机侧，由主机完成 APRS-IS 文本化与语义映射。设备侧不需要理解 APRS 语义。

### 15.1 能力与版本

- 新增能力位:
  - `CapAprsGateway`(建议使用 `HostLinkCapabilities` 新位，表示可提供 APRS-IS 所需的附加元数据)
- 设备在 `HelloAck` 中声明是否支持该元数据扩展。
- 若不支持，主机端必须降级为“仅做有限映射或不开启 iGate”。

### 15.2 配置项(主机侧配置为主)

APRS-IS 相关配置建议由主机侧管理(不要求设备理解 APRS)。若确需在设备侧持久化，也可通过 HostLink config TLV 扩展，但不是必须。

主机侧建议配置项:
- `AprsEnable`(bool)
- `AprsServerHost`(string, 默认 `rotate.aprs2.net`)
- `AprsServerPort`(uint16, 默认 `14580`，直连 TCP)
- `AprsIgateCallsign`(string)
- `AprsIgateSsid`(uint8)
- `AprsPasscode`(string)
- `AprsToCall`(string)
- `AprsPath`(string)
- `AprsFilter`(string, APRS-IS 登录过滤器)
- `AprsTxMinIntervalSec`(uint16)
- `AprsDedupeWindowSec`(uint16)
- `AprsPositionIntervalSec`(uint16)
- `AprsSymbolTable`(char)
- `AprsSymbolCode`(char)
- `AprsUseCompressed`(bool)
- `AprsEmitStatus`(bool)
- `AprsEmitTelemetry`(bool)
- `AprsEmitWeather`(bool)
- `AprsEmitMessages`(bool)
- `AprsEmitWaypoints`(bool)
- `NodeIdToCallsignMap`(map)
- `AprsSelfEnable`(bool, 设备端)
- `AprsSelfCallsign`(string, 设备端，仅设备自身 callsign)

注:
- 若将上述配置下发到设备侧，应与现有 HostLink config TLV 兼容(1 byte key + 1 byte len + value)。
- 当设备启用 `AprsSelfEnable` 且在 `meshtastic_User.id` 发布 callsign 时，主机侧必须优先使用设备侧 callsign；`NodeIdToCallsignMap` 仅作为回退方案。

### 15.3 AppData RX(基于 Meshtastic 业务数据)

设备端通过 Meshtastic 协议接收并上报 AppData。HostLink 需要传递 Meshtastic AppData 与接收元数据，主机侧完成 APRS-IS 文本化与语义映射。设备侧不需要理解 APRS。

事件类型建议:
- 复用现有 `EvAppData`，并在其后附加 RX 元数据 TLV(或新增 `EvAppDataRxMeta`)

AppData 必须包含:
- `portnum`
- `from` / `to` / `channel`
- `timestamp`
- `payload`(原始 Meshtastic 应用载荷，通常为 protobuf 或 UTF-8 文本)

要求:
- 必须提供 `direct` 判定来源，用于 qAR/qAO 决策。
- 必须标记 `from_is` 或等价字段，以避免回注。
- `payload` 必须原样传递，不得清洗或重写。
- 当设备声明 `CapAprsGateway` 时，`rx_meta_tlv` 必须随 EV_APP_DATA/EV_RX_MSG 一并上报。

#### 15.3.1 AppData 仍缺少的接收元数据(必须补充)

仅靠 Meshtastic AppData(含 from/to/channel/flags/timestamp/payload)不足以满足 iGate 语义与去重/限速需求。设备侧必须通过 HostLink 额外提供以下 RX 元数据(可用扩展字段或 TLV 追加):

**必须字段(缺一不可):**
- `rx_timestamp_utc` 或 `rx_timestamp_s`(UTC/GPS 时间优先): 用于 APRS 包时间与去重窗口
- `direct` 判定(直收/非直收): 用于 qAR vs qAO
- `rx_origin` 与 `from_is`: 标记来源是否为“RF/mesh”或“外部注入”，避免回注环路
- `rx_rssi_dbm` 与 `rx_snr_db`: 便于质量评估与调试
- `hop_count` 或 `rx_relayed`: 辅助判定是否经由网内转发
- `packet_id` 或 `seq`: 便于跨链路去重

**建议字段(可选):**
- `rx_channel`/`freq`/`bw`/`sf`: 便于多信道/多制式分析

#### 15.3.2 主机侧 APRS-IS 映射所需的 Meshtastic 数据(必须上报)

为了将“与 APRS 无关”的 Meshtastic 数据映射成 APRS-IS，设备侧必须上报以下类型的 AppData(或等价事件):

- 位置(Position): 来自 `POSITION_APP` 或 GPS 事件，需含 lat/lon/alt/speed/course/time
- 文本消息(Text): 来自 `TEXT_MESSAGE_APP`，需含文本与发送者/接收者标识
- 遥测(Telemetry): 来自 `TELEMETRY_APP` 或环境/设备指标，需含数值与时间
- 节点信息(NodeInfo): 来自 `NODEINFO_APP`，用于映射 callsign/符号/显示名
- 状态(Node Status): 来自 `NODE_STATUS_APP`，用于 APRS 状态帧 `>` 或注释
- 路点/对象(Waypoint): 来自 `WAYPOINT_APP`，用于 APRS Object/Item

若设备侧无法上报以上类型，则主机侧只能输出降级 APRS 数据(例如仅网关自身位置或仅状态摘要)。

### 15.4 可选: APRS TX(本期不做)

本期不做 RF 侧发射，以下仅作为可选扩展。若后续需要请求设备发射 APRS，且设备不接受原始 AX.25 帧时，HostLink 命令应传递 APRS 字段或 APRS 文本行。

命令类型建议:
- `CmdTxAprs`(新增 HostLinkFrameType 0x16 或按空位分配)

Payload 推荐格式(与 RX 对应):

**方案 A: 结构化 APRS 字段(推荐)**
```
u8   tx_flags           // bit0: ack_required, bit1: via_digi
u8   channel
u8   src_len
u8[] src_callsign
u8   dst_len
u8[] dst_tocall
u8   path_count
u8[] path_items         // 每个 path: u8 len + ASCII token
u16  info_len
u8[] info_field
```

**方案 B: APRS 文本行(可用)**
```
u8   tx_flags
u8   channel
u16  line_len
u8[] aprs_line          // "SRC>DST,PATH:INFO"
```

要求:
- 设备必须返回 `Ack` 并提供 `EvTxResult` 或专用 `EvAprsTxResult`。
- 设备端应进行基本合法性校验(字段长度、callsign 格式、path 数量)。

### 15.5 AppData 透传策略(推荐，最小设备改动)

不新增 HostLinkFrameType 时，建议直接复用现有 `EvAppData`，保持 Meshtastic payload 原样透传，由主机侧按 `portnum` 解码并映射为 APRS-IS:

- payload 必须原样透传
- portnum 必须保留，用于主机侧选择解码器
- 如需扩展 RX 元数据，可在 `EvAppData` 后追加 TLV，或新增 `EvAppDataRxMeta`

注意:
- 设备侧无需解析 APRS，也无需构造 APRS 文本行。

### 15.6 去重与环路提示

设备端应提供辅助信息以优化去重/环路:
- `rx_flags.from_is` 或 `rx_origin` 字段
- `rx_seq`(可选)用于重复帧检测

### 15.7 时间与位置一致性

由于 APRS 广泛使用时间戳:
- 设备端应支持 `CmdSetTime` 并维持时间精度。
- 若具备 GPS，可在 APRS RX 事件里标明使用 GPS 时间。

### 15.8 兼容性要求

- 不应破坏现有 HostLink 帧编码(保持版本号与 SOF/CRC 规则)。
- 新增帧类型必须允许忽略(旧主机/旧设备应能安全忽略未知类型)。

## 16. 验收要点(摘要)

- 能解析并重编码所有主要 APRS 位置类型(含 Mic-E 与 DAO)。
- RF→IS 路径重写正确，q 架构正确，且不会注入 RF 扩散路径。
- 3rd-party encapsulated traffic 可正确解析与防环路。
- Message/Telemetry/Object/Item/Status/Weather 全语义可查询。
- iGate 具备去重、限速、缓存、重连、可观测性。
- HostLink 提供 Meshtastic AppData 与必要 RX 元数据，TX 为可选扩展。
