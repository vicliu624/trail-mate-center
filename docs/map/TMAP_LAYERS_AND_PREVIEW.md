# 三种 TMAP 制作、桌面展示与资源预算

## 数据流

- OSM：本地 PBF → 磁盘几何库 → 无文字底图 → RGB565LE；完整地点、别名、搜索与显示标注进入 OSM TMAP。
- 地形：同一 PBF 土地覆盖/道路 + Tilezen Terrarium 高程 → 无文字山体阴影 → RGB565LE 地形 TMAP。
- 卫星：既有 Esri World Imagery 来源及 `satellite-cache/z/x/y.jpg` → Center 解码 → RGB565LE 卫星 TMAP。
- 三种底图共用 OSM TMAP 的独立 POI/名称；地形和卫星包不复制完整 POI 搜索库。

Tilezen 高程 PNG 是数值编码，不是地图图片。高度（米）=R×256+G+B/256−32768。生成时采用邻接高程 halo、双线性取样和按纬度修正的像素地面间距；阴影采用西北方向、45°高度角。DEM 输入最高取 z15，z16–18 从父级取样，不声称产生新的高程细节。源高程的实际分辨率和质量仍限制地形结果。

高程来源：<https://registry.opendata.aws/terrain-tiles/>；格式：<https://github.com/tilezen/joerd/blob/master/docs/formats.md>；来源署名列表：<https://github.com/tilezen/joerd/blob/master/docs/attribution.md>。影像来源复用现有 Center 配置；包内记录对应来源。PBF 本身不能生成卫星影像。

## 桌面显示

主地图的 OSM、地形和卫星图层使用 `TmapMapProvider`，不再从在线底图或散落 PNG/POI JSON 展示。读包方式为索引 → offset → 原生像素；名称从 `ReadDisplayAnnotations` 获取，并以 stable ID 去重。旧版包缺少段 43/44 时参考 reader 可读取旧标注链。

Mapsui 的 raster feature 接口接收编码图像，所以桌面适配层把原生像素转换成内存 PNG；不落盘。此转换只发生在电脑端，ESP 固件仍直接显示原生像素。独立文字保持独立 feature，未烘焙回底图。

地图包路径登记在“文档/TrailMateCenter/maps/tmap/packages.json”，只存路径，不复制大文件。默认还扫描该目录的 `osm`、`terrain`、`satellite` 子目录和旧平铺目录。PBF 导出自动登记成品；外部成品可用 `TmapTool register file.tmap` 登记。移动包后重新登记新路径。只安装了 OSM 时，地形/卫星图层没有对应像素，不能凭空显示这两种地图。

当前桌面底图接入不等于已经完成交互式地点搜索或道路导航。参考 reader 的完整检索 API 保留；显示副本的 79 字节名称上限不限制完整名称和别名。

## 连续显示标注

段 43：4096 字节页、64 字节页头、176 字节固定行，每页 22 行；段 44：瓦片 Morton 键 → first_row/count 的 B+ 树。每条显示行包括 stable ID、锚点、kind/priority、80 字节名称区、最多 8 个短路径点。

读取 A 条标注的 I/O 为 O(log_B T+ceil((s+A)/22))，CPU 为 O(A)，其中 T 为标注瓦片数、s 为首行页内槽。避免为每条名称访问 POI、name 和 string 的不同页。完整搜索表 20–24、30–32 和旧标注段 40–42保留。

## 内存与磁盘

- 高程处理只保留当前瓦片的最多 9 个 256×256 float 高程块，约 2.25 MiB；相邻瓦片共用仍需要的块。
- 图片逐张处理；HTTP 单瓦片响应上限 4 MiB，不把全球数据载入内存。
- 每个 TMAP builder 的 SQLite 缓存预算为 16 MiB，索引和排序在磁盘；多种底图分别有 staging 文件。
- 原生 RGB565 每瓦片 128 KiB。常驻设备像素仍受固件的 3 MiB PSRAM 预算约束，格式没有增加设备高程处理。
- 多图层制作会占用多个原生像素暂存文件，发布时还需要目标文件空间；下载的压缩源缓存也占磁盘。制作大范围前必须按选中图层估算，不能把输出体积当成峰值工作空间。

`fast-labels-upgrade file.tmap` 在电脑原地追加小段，保存 8 KiB+原长度的回滚日志，不复制整个包。`fast-labels-install prepared.tmap destination.tmap` 给匹配旧包增量安装新增段；适合慢 USB，只传新增段与包头。备份日志不能在安装中删除；失败/断电后再次执行同一安装会先恢复。安装期间保持设备 USB 存储独占，完成后退出并重建地图会话。

每包在完整校验后发布；多包任务若在后续发布环节失败，已成功发布的包不会自动回滚。任务结果的 `Packages` 列出具体成品，顶层 `FileBytes`/`TileCount` 是总计。
