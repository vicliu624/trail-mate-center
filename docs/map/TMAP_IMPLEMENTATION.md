# Center 端 TMAP 制作说明

## 1. 本次实现范围

Center 已有可运行的 `.tmap` 制作链：本地 OSM PBF → 无文字原始像素 → 地点及名称 → 磁盘索引 → 单文件 → 校验 → 发布。桌面“地图包制作”和 `tools/TmapTool` 共用 Core 实现。

当前生成的是 **TMAP v1，normalization_profile=2**。这是供设备端接入的格式实现，现有设备固件不会因为扩展名相同就自动支持它。

| 能力 | 当前状态 |
|---|---|
| OSM / Terrain 底图 | PBF 本地渲染为 256×256 RGB565LE |
| 卫星影像 | 可导入已有、具备使用权限的 PNG；PBF 不生成影像 |
| 等高线 | 桌面导出复用已有缓存，存 RGBA8888；命令行可导入已有 PNG |
| 单区域单文件 | 底图、POI、名称索引、空间索引、独立标注均在文件内 |
| 搜索 | 精确、前缀、Unicode 单字/双字片段；支持源数据提供的别名 |
| POI 读取 | stable_id B+ 树、行号直接寻址、空间范围查询 |
| 独立文字 | 底图不绘制名称；标注引用 POI，另存锚点和短路径 |
| 目标坐标 | 搜索结果包含 WGS84 经纬度，可以交给目标指引功能 |
| 道路路径导航 | **未实现**路网提取、转向限制、跨包路网和路径规划，routing 位为 0 |
| 跨包使用 | 已输出包身份、系列、图层、范围、缩放和稳定 POI ID；设备端跨包选择、去重、搜索调度待接入 |

“全离线地图可搜索”仍受源数据和分类器覆盖范围约束。当前导入命名节点及现有标注分类器支持的 way/relation；没有承诺所有 OSM 对象、所有地址或完整道路别名。显示标注限额不用于截断已收录的搜索库。

## 2. 分包规则

| Tier | 缩放 | 典型文件 |
|---|---|---|
| `World` | 0–7 | `world-z0-7.tmap` |
| `LargeCountry` | 8–12 | `cn-z8-12.tmap`、`us-z8-12.tmap`、`ru-z8-12.tmap`、`ca-z8-12.tmap`、`au-z8-12.tmap` |
| `AdministrativeRegion` | 13–17 | `cn-yn-z13-17.tmap`；美国州、俄罗斯联邦主体、加拿大省/地区、澳大利亚州/领地对应文件 |
| `Custom` | 用户指定，PBF 渲染 0–18 | 城市、景区等 |

预设会校验缩放，避免把国家包误生成为 0–17。每份计划必须提供实际边界和源 PBF；程序不通过国家名称猜测行政区边界，也不自动下载整个国家数据。

`PackageKey` 是同一区域、同一分包职责的稳定身份；更新该包保持 key 并增加 `Revision`。`Series` 相同代表发布方承诺样式/类别协议兼容。不同国家和省使用不同 key；版本号只在同一个包身份内比较。

现阶段日期线两侧的区域须拆成两个包；不能把 `West > East` 传入。俄罗斯、美国跨日期线地区尤其需要按实际边界处理，不能为了一个文件名而遗漏覆盖范围。支持一个逻辑区域多覆盖矩形及统一单包生成，是后续扩展项。

## 3. 使用方法

### 3.1 桌面

在地图包制作窗口选择范围和本地 PBF，启用 TMAP 单文件导出，填写文件名、稳定包 key、国家代码及行政区代码，选择世界/大国/下级行政区预设，然后导出。PBF 路径为必填。PBF 制作不支持勾选卫星影像。

界面的容量估计是未去重 RGB565 底图载荷；相同像素共享会减少这部分体积，索引、名称、标注和等高线会增加体积。任务保存 TMAP 制作计划；失败或取消后重试会从头构建 TMAP，不做页级断点续写。

### 3.2 命令行

在仓库目录运行：

```powershell
dotnet run --project tools/TmapTool -- build tools/TmapTool/examples/local-area.json
dotnet run --project tools/TmapTool -- verify artifacts/tmap/local-area.tmap
dotnet run --project tools/TmapTool -- search artifacts/tmap/local-area.tmap "水源" Substring
dotnet run --project tools/TmapTool -- search artifacts/tmap/local-area.tmap "昆明" Prefix
dotnet run --project tools/TmapTool -- batch plans.json artifacts/tmap
dotnet run --project tools/TmapTool -- import-rasters maps artifacts/tmap/imported.tmap metadata.json
```

`build` 的 PBF 与输出路径相对计划文件。`batch` 输入同样计划的 JSON 数组，PBF 相对数组文件，统一输出目录相对当前工作目录。每包独立发布，后一个失败不会回滚已经完成的包。批量文件名必须唯一，更新相同文件名会替换原文件。

`import-rasters` 输入目录采用 `base/osm/z/x/y.png`、`base/terrain/...`、`base/satellite/...`、`contour/major-500/...` 等已有布局。像素必须为 256×256，文字应由输入方预先移除。该命令只导入像素，不会从图像识别 POI；组合已有像素和外部 POI 时使用 `TmapBuilder.AddPlace` API。

CLI 返回码：0 成功，1 错误，2 参数不匹配，130 取消。构建进度写 stderr，结果 JSON 写 stdout。按 Ctrl+C 可取消。

### 3.3 OSM 世界 0–7 级成品

`world-osm` 接受 Shortbread v1.1 格式的全球 OSM 矢量 MBTiles，以矢量中的海洋、水面、陆地覆盖、行政边界和主要道路生成无文字 RGB565 底图，并将提供的地点、行政区和水体名称写入独立搜索及标注索引。

```powershell
versatiles convert https://download.versatiles.org/osm.20260608.versatiles artifacts/world-osm-source/world-z0-7.mbtiles --min-zoom 0 --max-zoom 7
dotnet run --project tools/TmapTool -c Release -- world-osm artifacts/world-osm-source/world-z0-7.mbtiles artifacts/tmap/world-osm-z0-7.tmap
```

本次使用 VersaTiles 发布的 OSM 数据快照 `2026-06-08`；只下载 0–7 级，源矢量归档约 120 MiB，避免处理完整 planet PBF。此入口不读取 Natural Earth 文件。源归档为稀疏瓦片集：无矢量要素的瓦片使用陆地背景（海洋本身由 ocean 多边形表达），南极无要素区域使用冰雪背景。最终显式输出所有 21845 张瓦片，各级数量为 1、4、16、64、256、1024、4096、16384。

搜索库只涵盖该低缩放矢量产品提供的名称，不能称为全球完整 POI 数据库。原始 OSM 已有的低缩放未输出名称、商家、地址等仍需详细区域包提供。名称包括源提供的本地名和多语言名；源元数据并不保证每个地点都有中文名。坐标由瓦片坐标反算，存在矢量量化误差。

源 feature_id 可能包含生成器重编号，因此注册独立命名空间 3，URI 指向固定源归档及 Shortbread feature ID 协议；没有将这些 ID 冒充原始 OSM 对象 ID。后续与原始 PBF 生成的省级包去重需要来源映射。输出 `world-osm-z0-7.manifest.json` 记录源、各级数量、索引统计和限制，预览图是无文字底图。

### 3.4 中国 8–12 级成品

中国包沿用世界包的 `2026-06-08` OSM 快照、`trail-mate-osm-shortbread-v1` 系列与来源命名空间 3，缩放只含 8–12。覆盖范围为 Geofabrik `china.poly` 与 `taiwan.poly` 提取多边形的并集，包含大陆、港澳台及提取多边形覆盖的海岛和近海区域。这是数据提取范围，不是官方行政边界；边缘瓦片保留整张图像，可能带有少量邻近区域作为地图上下文。

```powershell
versatiles convert https://download.versatiles.org/osm.20260608.versatiles artifacts/china-osm-source/china-z8-12.mbtiles --bbox 73.41788,14.27437,134.8036,53.65559 --min-zoom 8 --max-zoom 12
dotnet run --project tools/TmapTool -c Release -- china-plan artifacts/china-osm-source
dotnet run --project tools/TmapTool -c Release -- china-osm artifacts/china-osm-source/china-z8-12.mbtiles artifacts/tmap/china-osm-z8-12.tmap artifacts/china-osm-source
dotnet run --project tools/TmapTool -c Release -- china-check artifacts/tmap/china-osm-z8-12.tmap artifacts/china-osm-source
```

多边形文件来自 `https://download.geofabrik.de/asia/china.poly` 与 `https://download.geofabrik.de/asia/taiwan.poly`。文件应与成品制作清单一起保留；覆盖改变时以保存的多边形重现此次瓦片列表。

各级瓦片数量为 z8=799、z9=2975、z10=11497、z11=45186、z12=178958，总数 239415；完全不去重时 RGB565 载荷为 31380602880 bytes。制作器用预处理多边形判断瓦片相交，并过滤地点锚点。像素去重仅合并完全相同的同层像素；不会降低像素尺寸、颜色精度或省略缩放级别。

`china-check` 逐张读取预期瓦片和标注，检查 RGB565、缩放 mask、CRC，并执行主要城市搜索；同时输出全国概览与昆明、北京、香港、台北的 z12 局部预览。国家级搜索仍限于源矢量提供的点名称，不包含完整商家、地址和路网导航，线形道路名称目前未纳入此入口。

该单文件可能超过 4 GiB，使用时文件系统和设备读文件 API 都须支持超过 4 GiB 的文件；FAT32 单文件上限为 4 GiB−1。分段复制同一个 `.tmap` 不能让现有格式自动绕过此限制。国家包 64 位偏移的桌面读写校验与设备真机验收分别记录。

示例计划是一个小范围自定义包，需替换源 PBF 路径后使用。省级 z13–17 可能很大，不应直接使用世界矩形套用该缩放。

## 4. 文件结构

所有整数 little-endian，地理坐标为 WGS84 × 10^7 的 i32；文件偏移和段内偏移为 u64。内部 SQLite 排序键使用 big-endian 保持字典序；写入数字索引时转换回 little-endian。

```text
0                    Header：256 bytes
256..4095            保留零填充
4096                 SectionDirectory：每项 64 bytes
下一 4096 对齐位置    META、LAYERS
之后逐段 4096 对齐    POI / 名称 / 搜索 / 空间 / 标注 / 各层瓦片索引与像素
```

Header：magic@0=`TMAP\r\n\x1a\n`，major:u16@8=1，header_size:u32@12=256，endian@16=0x01020304，page_size@20=4096，required_features:u64@24，file_size:u64@32，directory_offset:u64@40=4096，directory_count:u32@48，entry_size:u32@52=64；package_id@56、build_id@72、series_id@88 各 16 bytes；revision:u64@104；source_epoch:u64@112 当前为 0；bbox@120；raster_zoom_mask:u32@136；capabilities:u32@140；meta_offset/length:u64@144/152；header_crc32c:u32@160，directory_crc32c:u32@164；坐标 profile:u32@168=1；名称 profile:u32@172=2。其他保留字段为零。

包和系列 ID 分别取 key/Series 的 SHA-256 前 16 bytes；build_id 每次生成 UUID。它们不是 POI ID。capability bit0=像素，bit1=搜索，bit2=标注，bit3=路网。空搜索/标注段合法，不代表源覆盖完整。

目录项：type:u32@0，schema:u16@4=1，flags:u16@6（bit0 必需、bit1 分页），id:u32@8，owner_layer:u32@12，offset:u64@16，length:u64@24，count:u64@32，root:u64@40；剩余保留。

| Type / ID | 内容 | 布局 |
|---|---|---|
| 1 | META | TLV，每项 tag:u16、flags:u16、length:u32，尾部对齐 8 |
| 2 | LAYERS | 96 bytes / 图层 |
| 10 / 1000+2×layer | 瓦片 B+ 树 | 32 bytes / 叶记录 |
| 11 / 上项+1 | 原始像素 | 连续载荷，无页头 |
| 20 | POI_TABLE | 96 bytes / 行，42 行 / 页 |
| 21 | POI_ID_INDEX | stable_id → POI row |
| 22 | NAME_TABLE | 48 bytes / 行，84 行 / 页 |
| 23 | STRINGS | u16 字节长、u16 flags、UTF-8，不跨页，最长 512 bytes |
| 24 | SPATIAL | Morton 排序后批量构建的空间树，32 bytes / 项 |
| 30 | NAME_DICT | 完整规范化 UTF-8 名称 → PostingRef |
| 31 | GRAM_DICT | 单/双 Unicode scalar 数字键 → PostingRef |
| 32 | POSTINGS | 目录页 168 项，数据页 504 个 u64 name_id |
| 40 | ANNOTATION_INDEX | tile_key → 标注列表引用 |
| 41 | ANNOTATION_DATA | 40 bytes / 标注，每页最多 100 条，每瓦片最多 200 条 |
| 42 | GEOMETRY | 独立标注短路径，每条最多 8 个 E7 经纬点 |

META 包含范围、缩放、类别字典、OSM 命名空间字典、profile 说明及统计。bbox 为稀疏覆盖外包矩形，存在性以索引命中为准。当前类别编号按生成器类别集合排序分配，因此升级分类器时须更新 Series，或按 META 字典映射，不能盲目比较两个包的类别整数。

图层记录：layer:u32@0、semantic:u16@4、tile_size:u16@6、zoom_mask:u32@8、pixel_profile:u16@12、label_policy:u16@14=0、index_section:u32@16、pixel_section:u32@20、annotation_section:u32@24、flags:u32@28、style_id:16 bytes@32、bbox@48。样式 ID 当前由 Series 派生，改变实际渲染规则时必须改变 Series。

通用页：magic:u32@0=0x31475054；type:u16@4；level:u16@6；self_offset:u64@8；count:u32@16；entry_size:u16@20；next:u64@24；numeric_min/max:u64@32/40；crc32c:u32@48；页头共 64 bytes。CRC32C 覆盖整个 4096-byte 页，计算时 CRC 字段归零。标准检查串 `123456789` → `0xE3069283`。

非空树的第一页是描述页：key_kind:u16@64、leaf_type:u16@66、max_key_bytes:u16@68、count:u64@72、root:u64@80、height:u32@88。空 B+ 树段长度为 0、root=0；不能对其读取页 0。

数值树内部项为 max_key:u64 + child_offset:u64；叶项瓦片键为 `(z << 58) | morton29(x,y)`。叶载荷为 data_offset:u64、stored_length:u32、decoded_length:u32、codec:u16、flags:u16、crc32c:u32。codec=1 为 RGB565LE（131072 bytes），2 为 straight RGBA8888（262144 bytes）。仅免除 PNG/JPEG 图像解码；设备仍需读取、CRC、内存复制，显示后端若有字节序要求还需转换。

文本树使用页尾 u16 slot 和完整键：key_bytes:u16、value_bytes:u16、key[]、value[]；内部 value 为子页地址。按数据集中最大键长度保守计算容量，避免变长项跨页。叶链用于前缀扫描。

POI stable_id 为 namespace:u32=1、OSM type:u16（1 node / 2 way / 3 relation）、reserved:u16、source_id:u64。以完整 16 bytes 不透明键排序。POI 记录依次包含 ID@0、lat/lon@16/20、category@24、kind flags@28、primary_name_id@32、first_name_id@40、name_count@48、importance:u16@56、min/max_zoom:u8@58/59；其余为零，路网锚点尚未生成。行号从 1 开始，直接寻址为 `section + floor((row-1)/42)*4096 + 64 + ((row-1)%42)*96`。

名称记录：poi_row:u64@0、display_ref:u64@8、normalized_ref:u64@16、flags:u16@28（1 主名、2 别名）、normalized/display 字节长:u32@32/36。索引的文档单位是 name_id，同一 POI 的不同别名不能拼接组成一次片段命中。

PostingRef：directory_offset:u64@0、total:u64@8、block_count:u32@16、codec:u16@20=0，其余保留，共 32 bytes。目录项为 first_name_id/last_name_id/data_offset 各 u64；通过 last_name_id 二分定位数据块。Gram 编码为 `(length << 42) | (scalar1 << 21) | scalar2`，单字 scalar2=0。

标注记录：poi_row:u64@0、kind:u16@8、min/max_zoom:u8@10/11、priority:u16@12、anchor_lat/lon:i32@16/20、geometry_ref:u64@24。标注索引叶 value 为首数据页:u64、有效记录字节数:u32，余下保留。几何对象为 type:u16=1、payload_bytes:u32@4、point_count:u32@8、16-byte 头，后接 lat/lon:i32 对。字体不打入文件，由设备独立提供。

## 5. 算法与复杂度

令 T=瓦片数，P=POI 数，N=名称数，G=gram 种类，E=全部 name-gram 关联数，B=页容量，D=查询候选名称数，A=每个候选 POI 的名称数，L=名称长度，K=返回数。

| 操作 | 当前算法与复杂度 |
|---|---|
| 构建 | SQLite 磁盘暂存、排序和索引，约 O(T log T + P log P + N log N + E log E)，还需完整渲染和写入载荷 |
| B+ 树批量构建 | 输入已排序后 O(n)，逐层外部引用文件，常驻少量页 |
| 瓦片读取 | O(log_B T) 页查找 + 固定 128/256 KiB 载荷 |
| stable_id 查 POI | O(log_B P) + 固定行读取 |
| row 查 POI | O(1) 地址计算，字符串仍需对应页读取 |
| 精确名称 | O(log_B N + D) 索引候选访问，再读取/排序匹配 POI |
| 前缀名称 | O(log_B N + 遍历名称数 + D)，通过叶链顺序扫描 |
| 片段搜索 | O(q log_B G)，取最多 8 个最稀有 gram，以最短倒排为驱动，其他列表按块二分探测；候选再做完整名称检查 |
| 排名 | 候选逐一检查约 O(D × A × L + D log K)，有距离时计算球面距离；仅保留 K 个结果 |
| 空间范围 | 平均由包围盒剪枝；最坏 O(P)，不能保证所有分布都达到 O(log P + 输出数) |
| 按瓦片标注 | O(log_B T + J)，J≤200；不扫描全 POI |

单字、高频片段、很短前缀都可能遍历大量名称；返回 K 项不等于只检查 K 项。当前参考阅读器为了正确排名完成全候选扫描，可取消，但没有设备端时间片/续查游标。排名顺序：精确优先于前缀、前缀优先于片段；随后距离（提供坐标时）、重要度、stable_id。相同 POI 多个名称不会重复占用结果。

Reader 页缓存固定 32×4096=128 KiB，另有当前像素、候选临时对象、查询串及 O(K) 结果。Builder 私有 SQLite 页缓存目标 16 MiB，temp_store=FILE；PBF 几何导入和渲染有自己的内存开销，不能据此声称整个制作进程只用 16 MiB。

## 6. 名称兼容协议

profile 2 为 `portable-scalar-v1`：全角 ASCII → ASCII、ASCII A–Z → a–z、固定空白集合折叠并去首尾，其余 Unicode scalar 原样保留。空白集合：U+0009–000D、0020、0085、00A0、1680、2000–200A、2028、2029、202F、205F、3000。

它不是完整 Unicode NFKC_Casefold；不隐式支持拼音、简繁转换、俄文字母大小写或重音消除。以后要支持这些能力，应增加新规范化 profile 和对应名称索引，不能静默改变 profile 2。设备查询必须采用同一 profile，未知必需 profile 应拒绝搜索能力。

## 7. 完整性、容量与发布

生成器在私有 GUID 临时目录暂存数据，完成后在目标目录写唯一 `.tmp`；构造 header/directory CRC、页 CRC 和每瓦片 CRC；完整读取分页段与像素校验后 flush，再替换最终文件。取消或发布前失败保留原文件。重试会重新生成，临时目录在正常异常清理路径删除；进程强杀可能留下临时目录。

`verify` 校验 header、目录、段边界、所有页 CRC 和像素 CRC；不等同于全量名称倒排语义审计，不证明数据源完整性，也不提供发布者认证。当前没有为 META/LAYERS 额外计算段内容 CRC；发行时可另行提供整个文件的 SHA-256。

原始像素以空间换设备 CPU。全球 z0–7 共 21845 个瓦片，单 RGB565 层未去重时约 2.67 GiB，尚未包含 POI 和索引；第二层近似再翻倍。制作中国包时已增加同层、同 codec 的完全相同像素块共享：SHA-256 检索后再逐字节核对，多个瓦片叶记录可以指向同一载荷。此机制不改变文件格式、不引入像素解码；常驻内存无需保存全部哈希，去重表位于 SQLite。全国/全省高缩放需先估算存储，`MaximumOutputBytes` 可阻止超预算发布；最终大小也再次检查。

偏移支持 64 位。全球 0–7 和中国 8–12 已实际制作，并完成桌面端完整瓦片、索引页 CRC 和检索校验；中国文件超过 4 GiB。设备文件系统和 API 也必须支持大文件，真机读写验收尚未完成；其他四个大国、全省 z17 与正式性能基准尚未执行。

## 8. 代码入口与接入顺序

- `TmapFormat`：常量、键、名称规范化、CRC。
- `TmapPages`：固定表、字符串页、外部排序后的 B+ 树构建。
- `TmapBuilder`：流式接收像素、POI、标注，磁盘暂存，生成单文件。
- `TmapPackExporter`：PBF/渲染/标注接入，RGB565/RGBA 转换，栅格目录导入。
- `TmapReader`：参考读取器，`ReadTile`、`ReadPoi`、`FindPoi`、`Search`、`QueryBounds`、`ReadAnnotations`。
- `tools/TmapTool`：自动化制作、批量制作、校验与检索。

后续顺序：设备按本 profile 实现 reader → 原始像素和独立文字接入 → 多包选择与搜索去重 → 完整 Unicode/地址与数据覆盖扩展 → 路网段格式、路径规划和跨包导航 → 国家规模与真机性能验收。道路导航不能仅靠一个 POI 坐标字段完成。
