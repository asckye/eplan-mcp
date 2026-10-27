# 电机配置测试材料

`MotorsPack` 是自主编写的非客户示例包，按负责人 O2 决定采用 EPLAN 自带示例领域中的电机起动方式、功率和电压；数值仅用于验证配置格式，没有提取或分发 EPLAN 项目、宏及客户材料，也不表示通用选型算法。

包包含 `config_schema/motors`、company 与 cabinet_type 两层。功率单位为 kW，范围为 0.06–250；电压单位为 V，不定义枚举。`InvalidConfig` 提供单位、枚举值、层名、来源和未知键的失败变体，不属于有效包。

Small 测试从程序集内存资源读取材料；Medium 测试复制到临时目录运行 pack check，并在测试失败变体时重新计算清单哈希，确保失败来自语义校验。

`Modules` 延续 O2 电机领域：motor-starter 具有 power/starter 参数、两个值集、供电及电机端口、四类验收检查和提取证据；company-a/company-b 是两套精确部件映射，motor-acceptance 是带来源的验收样例。这些自主编写的元数据没有验证真实选型；O2.TEST 部件号为虚构测试值。Medium 使用临时合成字节验证宏哈希，不把它作为有效原生宏，也不运行 EPLAN；原生结构、部件库存在性和实际验收留后续 PR。
