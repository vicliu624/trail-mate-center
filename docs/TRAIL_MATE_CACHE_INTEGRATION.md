# Trail-Mate 缓存地图对接说明（底图可选 + 等高线显隐）

本文面向 `/Users/liuweikai/Projects/trail-mate` 作者，目标是复用 `TrailMateCenter` 已缓存的数据，在 ESP32 设备端实现：

- 底图可选：`OSM / Terrain / Satellite`
- 等高线可见/隐藏：`On / Off`

同时考虑 ESP32 的 CPU/RAM/SD I/O 限制，采用最小侵入方案。

## 1. 现状结论

`TrailMateCenter` 当前缓存结构已经可复用，不需要重做缓存生成逻辑。

另外，`TrailMateCenter` 已提供地图内“框选缓存区域”能力，可直接生成该区域 `0~18` 缩放级别的完整缓存（底图 + 等高线队列），用于避免设备端缩放时出现空白瓦片。

缓存目录（macOS）：

- OSM：`~/Documents/TrailMateCenter/tilecache/{z}/{x}/{y}.png`
- 地形：`~/Documents/TrailMateCenter/terrain-cache/{z}/{x}/{y}.png`
- 卫星：`~/Documents/TrailMateCenter/satellite-cache/{z}/{x}/{y}.jpg`
- 等高线：`~/Documents/TrailMateCenter/contours/tiles/{major-xx|minor-xx}/{z}/{x}/{y}.png`

对应代码来源：

- `src/TrailMateCenter.App/ViewModels/MapViewModel.cs:869`
- `src/TrailMateCenter.App/ViewModels/MapViewModel.cs:893`
- `src/TrailMateCenter.App/ViewModels/MapViewModel.cs:920`
- `src/TrailMateCenter.App/ViewModels/MapViewModel.cs:693`
- `src/TrailMateCenter.App/Services/ContourTileService.cs:1032`

## 2. Trail-Mate 当前限制（需要适配）

`trail-mate` 当前地图加载路径写死为：

- `A:/maps/{z}/{x}/{y}.png`

关键代码：

- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:499`
- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:521`
- `/Users/liuweikai/Projects/trail-mate/src/ui/screens/node_info/node_info_page_components.cpp:377`

另外，缓存参数对 ESP32 已做过限制（这是好事）：

- 对象缓存：`TILE_CACHE_LIMIT = 12`
- 记录缓存：`TILE_RECORD_LIMIT = 48`
- 解码缓存：`TILE_DECODE_CACHE_SIZE = 12`

参考：

- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.h:17`
- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.h:18`
- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:26`

## 3. 推荐目录规范（SD 卡）

建议把 `A:/maps` 扩展成多图层结构，不改变 `z/x/y` 规则：

```text
A:/maps/
  base/
    osm/{z}/{x}/{y}.png
    terrain/{z}/{x}/{y}.png
    satellite/{z}/{x}/{y}.jpg
  contour/
    major-500/{z}/{x}/{y}.png
    major-200/{z}/{x}/{y}.png
    major-100/{z}/{x}/{y}.png
    major-50/{z}/{x}/{y}.png
    major-25/{z}/{x}/{y}.png
    minor-100/{z}/{x}/{y}.png
    minor-50/{z}/{x}/{y}.png
    minor-20/{z}/{x}/{y}.png
    minor-10/{z}/{x}/{y}.png
    minor-5/{z}/{x}/{y}.png
```

说明：

- 卫星图保持 `.jpg`，减少 SD 占用。
- 等高线保留透明 `.png`，用于叠加。

## 4. 设备端最小改动点（trail-mate）

### 4.1 复用已有 `map_source` 配置，增加选项

`map_source` 已经存在但目前只有 `Offline Tiles` 一个选项：

- `/Users/liuweikai/Projects/trail-mate/src/app/app_config.h:84`
- `/Users/liuweikai/Projects/trail-mate/src/ui/screens/settings/settings_page_components.cpp:880`

建议定义：

- `0 = OSM`
- `1 = Terrain`
- `2 = Satellite`

并把设置页 `kMapSourceOptions` 扩为 3 项。

### 4.2 新增等高线开关配置

在 `AppConfig` 中新增：

- `bool map_contour_enabled`

并在 settings 页面增加 Toggle（建议默认 `false`）。

### 4.3 抽象路径构造函数（替代硬编码）

新增统一函数：

- `build_base_tile_path(z, x, y, map_source)`
- `build_contour_tile_path(z, x, y, zoom, contour_profile)`

并替换所有 `A:/maps/%d/%d/%d.png` 字面量，至少覆盖：

- 主地图：`map_tiles.cpp`
- 节点详情小地图：`node_info_page_components.cpp`

### 4.4 主图层 + 叠加图层渲染策略

建议保持现有主图加载流程不变，只额外加“等高线叠加对象”：

- 主图层：现有 `MapTile` 机制（按 `map_source` 读取）
- 等高线层：仅在 `map_contour_enabled=true` 时创建 overlay image
- overlay 与主瓦片同屏坐标，独立对象但复用同一可见性判定

## 5. 等高线缩放档位（建议与 TrailMateCenter 对齐）

推荐沿用 `TrailMateCenter` 的档位逻辑：

- z<=7：无等高线
- z=8：`major-500`
- z=9：`major-200`
- z=10：`major-500` + `minor-100`
- z=11：`major-200` + `minor-50`
- z=12：`major-100` + `minor-50`
- z=13~14：`major-100` + `minor-20`
- z=15~16：`major-50` + `minor-10`
- z>=17：`major-25` (+ `minor-5` 可选)

ESP32 推荐默认只开 `major`，将 `minor` 作为高级选项，避免帧率下降。

## 6. 性能约束与建议（ESP32 重点）

### 6.1 I/O 与解码预算

- 保持现有 loader 节流策略（`12ms` 时间预算 + 每次最多 3 瓦片），不要放宽。
- overlay 建议每次最多再加载 1 张，且仅处理屏幕内瓦片。

参考：

- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:1237`
- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:1238`

### 6.2 缓存策略

- 主图解码缓存维持 8~12。
- 等高线可不做解码缓存或只给 2~4，优先保证主图流畅。
- 切换底图时清理 tile record 与 decode cache，防止混层占满 RAM。

### 6.3 存储体积控制

不要把整库复制到 SD。建议只导出关心区域 + 限制 zoom（例如 8~16）。

### 6.4 文件系统细节

忽略 `contours` 目录中的 `*.aux.xml` 文件，不需要复制到 SD。

## 7. 主机侧缓存处理流程（从 TrailMateCenter 到 SD）

建议流程（已支持在 UI 中框选 AOI）：

1. 在 `TrailMateCenter` 地图勾选“框选缓存区域”，拖拽框选目标区域。
2. 可选：在“缓存区域列表”中输入名称并保存，形成可复用的区域清单（可删除、可回填到当前框选）。
3. 点击“开始缓存”或“构建区域”，等待状态提示完成（默认覆盖 `0~18` 级；地形源最高 17 级）。
4. 从 `TrailMateCenter` 四类缓存目录中筛选并复制到 SD 结构（第 3 节）。
5. 可选生成 `manifest.csv`（用于统计和验收）。

XYZ 公式（WebMercator）：

- `x = floor((lon + 180) / 360 * 2^z)`
- `y = floor((1 - ln(tan(lat)+sec(lat)) / pi) / 2 * 2^z)`

`trail-mate` 当前瓦片坐标计算也是 WebMercator，可直接兼容：

- `/Users/liuweikai/Projects/trail-mate/src/ui/widgets/map/map_tiles.cpp:182`

## 8. 验收清单

1. 设置页可切换 `OSM/Terrain/Satellite`，重进页面后记忆生效。
2. 设置页可开关等高线，开关即时生效。
3. 无 SD 卡时依旧稳定，状态提示正常。
4. 连续缩放与平移 2 分钟，不出现卡死/明显掉帧。
5. 切换底图后不出现旧图残影（确认 cache 清理策略生效）。
6. 节点详情小地图跟随当前底图源，路径不再写死单一路径。

## 9. 我这边实现是否还需要改

结论：`TrailMateCenter` 现有缓存格式已经满足复用，不是阻塞项。

可选优化（非必须）：

- 增加一个“导出到 trail-mate SD 结构”的主机脚本，省去手工筛选复制。
- 在等高线生成阶段禁用 `aux.xml` 旁车文件，减少导出噪声文件。
