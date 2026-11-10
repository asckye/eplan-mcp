# IEC 规则插件签核表（IEC 60204-1、IEC 81346）

状态：**已签核**（2026-11-10，项目负责人）· 插件版本：`iec-60204-1` 1.0.0、`iec-81346` 1.0.0

两个插件是 EplanMcp 自带的审图规则（`packaging/plugin-libraries/builtin/`）。规则只有参数和条款号，不含标准原文。0.2.0 及以前是草稿，只能审 scratch 项目；1.0.0 签核后可以审其他项目（[M12 设计第 6 节](../design/2026-10-31-m12-review-and-generation.md)）。审图只检查收录的规则，结果干净不等于图纸符合标准。

## 签核结果

项目负责人 2026-11-10：“IEC规则里面除了N用蓝色，其他的按照你的建议来吧”。据此 1.0.0 相对 0.2.0 的改动：

- `neutral-light-blue`：N 线只认 `BU`，不再放行橙色 `OG`；官方项目照明回路的棕色 `BN` 也算违规。
- 多芯电缆的芯线（审图读到它属于某根电缆，新事实 `connection.cable`）不受 `power-black`、`neutral-light-blue`、`ac-control-red`、`dc-control-blue` 检查，颜色按电缆本身；两条绿黄规则对电缆芯线照样检查（保护芯也必须绿黄）。
- 其余规则、条款号、严重级别按下表，不变。条款号来自二手资料，按负责人决定保留；以后对照正式标准发现不符时再改，并重新签核。

插件 `pack.json` 已写入 `signoff`：`status: signed`、`signedBy: 项目负责人 asckye`、`signedAt: 2026-11-10`。

## 怎么签

1. 对照正式出版的标准，逐条核对下表的**条款号**和**取值**，在“签核”列写“同意”或改法。
2. 回答文末的**待定问题**。
3. 全部确认后告诉维护者，由维护者在插件的 `pack.json` 写入 `signoff`（`status: signed`、`signedBy`、`signedAt`），版本加一。签核后插件才能审 scratch 以外的项目。

## 验证方法（2026-11-10）

用两个插件逐页审查 5 个项目、129 页原理图（只读）：

- 官方 EES 的 4 个 IEC 项目：码垛 `EES_stacking_iec`（27 页）、S7-1500 `EES_s7_1500`（25 页）、充电桩 `EES_wallbox_iec`（7 页）、楼宇 HVAC `EES_hvac_iec_1`（49 页）。官方项目按 IEC 81346 编码，在它们身上报的问题大多是规则写错了（误报）。
- 我们画的通过式烘芯炉（21 页）。

| 轮次 | IEC 81346 报错（对象数） | IEC 60204-1 报错（对象数） |
|---|---|---|
| 第 1 轮（0.1.0） | 官方 10，烘芯炉 7 | 官方 63，烘芯炉 103 |
| 第 2 轮（0.2.0，按下表修改后） | 官方 0，烘芯炉 0 | 官方 9，烘芯炉 25 |

## IEC 60204-1:2016 导线颜色

级别：强制（mandatory）报错误，推荐（recommended）报警告。颜色代码按 EPLAN：`BK` 黑、`BU` 蓝、`DBU` 深蓝、`BUWH` 蓝白、`RD` 红、`OG` 橙、`GNYE` 绿黄、`BN` 棕、`WH` 白。

| 规则 | 级别 | 条款 | 适用于 | 要求 | 第 1 轮后的修改 | 签核 |
|---|---|---|---|---|---|---|
| `pe-green-yellow` | 强制 | 13.2.2 | 电位名 PE/PEN/PG/GND，或接 PE 连接点的线 | `GNYE` | 颜色写成数字（电缆芯号）时不检查 | 同意 |
| `green-yellow-only-protective` | 推荐 | 13.2.2 | 颜色是 `GNYE` 的线 | 必须是保护导体：PE/PEN 电位、PE 连接点，或接 PE 端子（`-XD1:PE`）、接地端子排（`-XE`、`-XPE`）、PE 母排（`-WE`） | 增加按两端端子识别保护导体（官方 3 个项目误报 9 处） | 同意 |
| `neutral-light-blue` | 推荐 | 13.2.3 | 电位名 N，或接 N 连接点的线 | `BU` | 电缆芯号不检查；签核时去掉 `OG`，N 只认蓝色 | 改：只认 `BU` |
| `power-black` | 推荐 | 13.2.4 | 电位 L1–L3，或接 L/T/U/V/W 连接点的线 | `BK`，或 `OG` | 增加 `OG`；电缆芯号不检查；继电器的 `T1`/`T3`（安全继电器测试输出）不算动力连接点 | 同意 |
| `ac-control-red` | 推荐 | 13.2.4 | 电位名为交流控制电压（如 `230V AC`、`~230V`） | `RD`，或 `OG` | **新增**（欧标、国标、德标版都有，IEC 版漏了） | 同意 |
| `dc-control-blue` | 推荐 | 13.2.4 | 电位名为直流控制电压（如 `24V`、`0V`） | `BU`、`DBU`、`BUWH`，或 `OG` | 增加深蓝 `DBU`、蓝白 `BUWH`（官方 S7-1500 的 0V 线用蓝白）；电缆芯号不检查 | 同意 |

第 2 轮仍报错、需要你判断的：

| 项目 | 规则 | 对象 | 说明 |
|---|---|---|---|
| EES_s7_1500、EES_hvac_iec_1 | `neutral-light-blue` | 照明 `EAA1` 的 N 线为 `BN`（棕） | 官方项目这样画；是否放行棕色 |
| EES_s7_1500 | `green-yellow-only-protective` | `XD5:5 → XD1:8` 的绿黄线 | 端子号看不出是 PE 端子，保留警告 |
| 通过式烘芯炉 | `neutral-light-blue` | N 线为 `WH`、`BK`（10 + 5 处） | 我们图纸的问题，签核后要改图 |
| 通过式烘芯炉 | `dc-control-blue` | 传感器电缆芯线 `BK`、`BN` | 见待定问题 1 |

## IEC 81346-2:2019 设备标识字母

全部为推荐级别，只检查主功能（`function.main`），按 EPLAN 功能类别取设备标识里的字母代码。

| 规则 | EPLAN 类别 | 允许的开头 | 第 1 轮后的修改 | 签核 |
|---|---|---|---|---|
| `motor-letter` | Motor | `M` | — | 同意 |
| `coil-letter` | Coil | `Q`（接触器）、`K`（继电器）、`B`（检测继电器） | 增加 `B`（官方 HVAC `BAA01`） | 同意 |
| `protection-letter` | Overload、MotorOverloadSwitch、CircuitBreaker、CurrentCircuitBreaker | `F`、`Q` | — | 同意 |
| `switch-letter` | Switch | `S`（手动）、`Q`（主开关、隔离开关）、`B`（位置开关等检测） | 增加 `Q`、`B`（官方 `QBA1`、`BGA1`） | 同意 |
| `lamp-letter` | SignalLamp | `P` | — | 同意 |
| `sensor-letter` | AnalogSensor、LightBarrier | `B` | — | 同意 |
| `converter-letter` | Converter、Source | `T`（转换器、电源）、`B`（测量变送器） | 增加 `B`（官方 `BGF1`、`BCB01`） | 同意 |
| `plc-letter` | PLCBox | `K`、`A`、`PH`（HMI）、`TA`（变频器等驱动） | 增加 `PH`、`TA`（官方 `PHA1`；烘芯炉的 HMI `16PH1`、变频器 `11TA1`） | 同意 |
| `connector-letter` | TerminalDefText、ConnectorDefText、Socket | `X` | — | 同意 |
| `cable-letter` | Cable、BusBarDefText | `W` | — | 同意 |

## 待定问题（已决定）

1. **多芯电缆的芯线颜色**：跳过功能颜色规则（已实现，见“签核结果”）。
2. **棕色 N 线**：不放行，N 只用蓝色。
3. **条款号**：保留现有条款号。
4. **严重级别**：同意现状。
