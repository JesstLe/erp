# 旧系统字段画像与初步映射矩阵

日期：2026-08-20
状态：真实字段画像与安全映射完成；B01 基础档案受控导入已实现，资金权益仍保持阻断
关联文档：[旧系统数据只读导出与迁移 PRD](legacy-system-data-migration-prd.md)

## 1. 本轮结论

本轮离线校验 2026-08-20 最终 AES-256-GCM 导出文件后，对 13 个模块、2545 条记录、213 个来源字段完成结构画像。画像报告只包含字段名、JSON 类型、出现/空值/去重数量、最大字符串长度、候选唯一键、格式类别和敏感级别，不包含任何来源字段值、值摘要、文件路径、账号、Cookie 或密钥。

当前可以确认：旧系统数据不能直接复制到新系统业务表。必须经过“来源留痕 → 暂存 → 标准化 → 关系映射 → 异常隔离 → 对账 → 受控导入”。其中顾客基础资料、组织和目录主数据大部分可以映射；会员资金、会员等级权益、充值规则、次卡规则和设施门店关系存在阻断项，未经补充数据或规则确认不得导入。

## 2. 画像证据摘要

| 模块 | 记录数 | 字段数 | 当前判断 |
|---|---:|---:|---|
| 顾客 `customers` | 2287 | 30 | 基础档案可转换；资金与权益字段阻断 |
| 门店 `stores` | 5 | 22 | 核心字段可映射；门店扩展资料需决定是否保留 |
| 员工 `employees` | 40 | 28 | 核心字段可映射；薪酬/押金及旧功能标志阻断 |
| 服务项目 `services` | 74 | 32 | 目录可转换；价格、提成和旧活动规则需确认 |
| 产品 `products` | 51 | 34 | 目录可转换；采购/销售价、促销和库存语义需确认 |
| 次卡目录 `service-passes` | 8 | 20 | 新系统缺少次卡商品模板，阻断 |
| 会员等级 `member-levels` | 23 | 21 | 卡类可映射；折扣/充值/积分规则模型不足，阻断 |
| 储值方案 `topup-plans` | 21 | 7 | 新系统只有储值交易，缺少储值方案模板，阻断 |
| 设施 `facilities` | 9 | 7 | 名称和状态可映射；没有门店关联，阻断 |
| 品牌 `brands` | 0 | 0 | 旧源无品牌记录，新租户品牌需人工建立 |
| 单位 `units` | 15 | 4 | 可作为产品单位字典 |
| 员工工种 `employee-trades` | 4 | 4 | 可作为岗位/工种字典 |
| 顾客来源 `customer-sources` | 8 | 4 | 可作为顾客来源字典 |

213 个字段中，画像识别出 40 个资金敏感字段、17 个个人信息字段和 12 个自由文本风险字段。三个字段包含 HTML 形态：`customers.member_code`、`products.goods_name`、`services.goods_name`。转换时只能使用受限解析器提取文本或标识，禁止把旧 HTML 原样写入或渲染到新系统。

## 3. 映射处理等级

| 代码 | 处理方式 | 规则 |
|---|---|---|
| `D` | 直接映射 | 语义一致，仍需长度、唯一性和外键校验 |
| `T` | 转换映射 | 格式、枚举、金额单位、HTML或日期需标准化 |
| `E` | 扩展/补模型 | 业务仍有价值，但新系统当前没有正式目标结构 |
| `A` | 仅原始归档 | 空字段、搜索辅助字段或已排除的旧功能，不进入日常业务表 |
| `R` | 人工确认/阻断 | 含义、关系或金额口径不能从字段画像可靠推断 |

所有 `R` 字段必须形成异常或决策记录。不能通过默认值、猜测字段名或把总额塞入任意余额账户来绕过。

## 4. 逐模块初步映射矩阵

### 4.1 品牌

`brands` 返回 0 条、0 字段。新系统 `organization_tenants` 代表品牌租户，本次不能从旧数据推导品牌。品牌代码、名称和首个 OWNER 必须由商户注册/人工建租户流程创建，不能生成虚构旧品牌记录。

### 4.2 门店

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `stores.shop_id` | `D` | 仅进入来源映射表，生成新 `organization_stores.id` |
| `stores.shop_code` | `D` | `organization_stores.code`；租户内唯一 |
| `stores.shop_name` | `D` | `organization_stores.name` |
| `stores.shop_stop` | `T` | 转换为 `StoreStatus.Enabled/Disabled`；先确认真假含义 |
| `stores.shop_time`, `stores.shop_date` | `T` | 保留旧创建/登记时间；不得冒充新系统操作时间 |
| `stores.shop_tel`, `stores.shop_addr`, `stores.shop_lat`, `stores.shop_lng`, `stores.shop_area`, `stores.shop_man` | `E` | 当前门店模型缺少联系电话、地址、坐标、区域和负责人资料；确认需要后以前向迁移增加门店档案字段 |
| `stores.shop_across`, `stores.shop_bdate`, `stores.shop_btime`, `stores.shop_price`, `stores.shop_start` | `R` | 名称不足以证明业务语义，需结合旧页面说明或业务人员确认 |
| `stores.shop_letter` | `A` | 旧搜索简码；新系统实时索引不依赖它 |
| `stores.shop_ismall`, `stores.shop_ismdd`, `stores.shop_ismsm` | `A` | 旧移动端/云端功能标志；不进入当前产品范围 |
| `stores.shop_memo` | `A` | 源数据全空，仅保留在加密原始档案 |

### 4.3 员工与工种

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `employees.emplee_id` | `D` | 来源映射键，生成新 `organization_employees.id` |
| `employees.emplee_code` | `D` | `organization_employees.employee_no` |
| `employees.emplee_name` | `D` | `organization_employees.display_name` |
| `employees.emplee_ework` | `T` | 通过工种映射转换为 `position_code`，不能直接使用显示名 |
| `employees.emplee_shop` | `T` | 通过门店映射生成 `organization_employee_stores`；先核对多店兼职信息是否另有端点 |
| `employees.emplee_end`, `employees.emplee_job` | `T` | 联合判断在职状态；不得只凭单一常量字段推断 |
| `employees.emplee_date`, `employees.emplee_time`, `employees.emplee_begin` | `T` | 作为旧入职/建档时间候选；字段含义确认后保留 |
| `employees.emplee_hand`, `employees.emplee_addr`, `employees.emplee_birthday`, `employees.emplee_sex`, `employees.emplee_idtype`, `employees.emplee_idcard`, `employees.emplee_parent` | `E` | 个人资料；当前员工核心模型未覆盖。若保留，需加密、字段权限和按需查看审计 |
| `employees.emplee_adviser`, `employees.emplee_isclock`, `employees.emplee_sms` | `R` | 可能代表顾问、考勤、短信能力；需决定是否转成岗位权限或配置 |
| `employees.emplee_deposit`, `employees.emplee_pay` | `R` | 资金/薪酬字段；不能写入员工资料或提成，需独立账务口径和历史明细 |
| `employees.emplee_elevel` | `R` | 旧员工等级含义待确认；不得直接映射角色权限 |
| `employees.emplee_account` | `A` | 源数据全空；旧账号和密码不迁移，新账号走重置/首次改密流程 |
| `employees.emplee_iscmd`, `employees.emplee_ismall` | `A` | 已排除的旧终端/云功能标志 |
| `employees.emplee_letter` | `A` | 旧搜索简码 |
| `employees.emplee_memo` | `E` | 自由文本；仅在确认有价值后加密保留并限制权限 |
| `employee-trades.ework_id` | `D` | 工种来源映射键 |
| `employee-trades.ework_code`, `employee-trades.ework_name` | `E` | 建议建立可配置岗位/工种字典，再由员工引用代码 |
| `employee-trades.ework_memo` | `A` | 源数据全空 |

### 4.4 服务项目

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `services.goods_id` | `D` | 来源映射键，生成 `catalog_service_items.id` |
| `services.goods_code` | `D` | `catalog_service_items.code` |
| `services.goods_name` | `T` | 安全提取旧 HTML 中的纯文本名称，禁止原样渲染 |
| `services.goods_status` | `T` | 转为 `CatalogItemStatus` |
| `services.goods_time` | `T` | 旧建档时间，仅作来源元数据 |
| `services.goods_sale`, `services.goods_vip` | `R` | 可能是标准价/会员价；确认口径后进入首个已发布价格版本，金额转为“分” |
| `services.goods_buy`, `services.goods_smin` | `R` | 可能是成本/最低价；不得按字段名直接进入售价或毛利 |
| `services.goods_bonus1A`, `services.goods_bonus1B`, `services.goods_bonus2A`, `services.goods_bonus2B`, `services.goods_bonus3`, `services.goods_deduct`, `services.goods_ework1`, `services.goods_ework2` | `R` | 旧提成与工种适用规则；新系统单一比例/固定金额模型不足以无损承接，需规则拆解和版本快照设计 |
| `services.goods_classe`, `services.goods_spec`, `services.goods_unit1`, `services.goods_shop` | `E` | 服务分类、规格、单位和适用门店；需要目录分类与门店适用关系模型 |
| `services.goods_bar1` | `E` | 旧条码/辅助编码候选；确认后作为外部编码，不替代主编码 |
| `services.goods_isbargain`, `services.goods_isdiscount`, `services.goods_isgive`, `services.goods_isscore` | `E` | 旧议价、折扣、赠送和积分资格；应转换为版本化规则，不放进核心项目布尔列 |
| `services.goods_iscmd`, `services.goods_ismdd`, `services.goods_ismsm` | `A` | 当前排除的旧终端、云商或营销功能标志 |
| `services.goods_letter` | `A` | 旧搜索简码 |
| `services.goods_area` | `A` | 源数据全空 |
| `services.goods_memo` | `E` | 少量自由文本；确认保留后做安全文本处理 |

### 4.5 产品与单位

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `products.goods_id` | `D` | 来源映射键，生成 `catalog_product_items.id` |
| `products.goods_code` | `D` | `catalog_product_items.code` |
| `products.goods_name` | `T` | 安全提取旧 HTML 中的纯文本名称 |
| `products.goods_unit1`, `products.goods_unit2` | `T` | 通过单位字典转换为 `unit_name`；明确主单位和换算关系 |
| `products.goods_status` | `T` | 转换为 `CatalogItemStatus` |
| `products.goods_time` | `T` | 保留为旧建档时间 |
| `products.goods_sale`, `products.goods_vip` | `R` | 标准/会员售价候选，确认后进入价格版本，金额转分 |
| `products.goods_buy`, `products.goods_ship`, `products.goods_smin` | `R` | 采购价、配送价或最低价语义待确认；不能直接进入售价 |
| `products.goods_deduct`, `products.goods_bonus3`, `products.goods_bonus4` | `R` | 旧提成/奖励字段；需确认基数、单位和适用人员 |
| `products.goods_bar1`, `products.goods_bar2` | `E` | 产品条码/辅助条码模型；`goods_bar2` 当前全空 |
| `products.goods_spec`, `products.goods_nums`, `products.goods_classe`, `products.goods_brand`, `products.goods_area`, `products.goods_client`, `products.goods_shop` | `E` | 规格、包装数量、分类、品牌、区域、供应商和门店适用关系；需要目录扩展/关联表，不能塞入名称 |
| `products.goods_isbuild` | `R` | 可能代表组装/配套商品，需确认库存行为 |
| `products.goods_isgift`, `products.goods_isgive`, `products.goods_isscore`, `products.goods_isdiscount`, `products.goods_isbargain` | `E` | 旧赠品、赠送、积分、折扣和议价资格；应进入版本化规则 |
| `products.goods_iscmd`, `products.goods_ismall` | `A` | 当前排除的旧终端/云功能标志 |
| `products.goods_letter` | `A` | 旧搜索简码 |
| `products.goods_memo` | `E` | 自由文本；确认后安全保留 |
| `units.unit_id` | `D` | 单位来源映射键 |
| `units.unit_code`, `units.unit_name` | `E` | 建议建立可配置单位字典；产品保留规范化单位名 |
| `units.unit_memo` | `A` | 源数据全空 |

### 4.6 次卡目录

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `service-passes.goods_id`, `service-passes.goods_code`, `service-passes.goods_name`, `service-passes.goods_status`, `service-passes.goods_time` | `R` | 新系统目前只有顾客已购次卡和核销流水，没有可销售“次卡商品模板”；必须先设计模板实体和版本 |
| `service-passes.goods_sale`, `service-passes.goods_spec`, `service-passes.goods_cihint`, `service-passes.goods_citerm` | `R` | 售价、次数和有效期候选；需确认单位、起算日和到期规则 |
| `service-passes.goods_deduct`, `service-passes.goods_smin`, `service-passes.goods_bonus3`, `service-passes.goods_bonus4` | `R` | 提成、最低价和奖励语义待确认 |
| `service-passes.goods_classe`, `service-passes.goods_shop` | `E` | 分类和门店适用关系；`goods_shop` 当前全空 |
| `service-passes.goods_isbargain`, `service-passes.goods_isdiscount` | `E` | 议价/折扣资格应进入次卡模板规则 |
| `service-passes.goods_letter` | `A` | 旧搜索简码 |
| `service-passes.goods_area`, `service-passes.goods_memo` | `A` | 当前全空，仅原始归档 |

### 4.7 会员等级与储值方案

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `member-levels.iclevel_id` | `D` | 来源映射键，生成 `membership_card_types.id` |
| `member-levels.iclevel_code`, `member-levels.iclevel_name` | `D` | `membership_card_types.code/name` |
| `member-levels.iclevel_status` | `T` | 转换为卡类 `Published/Disabled` |
| `member-levels.iclevel_opmoney`, `member-levels.iclevel_opbonus`, `member-levels.iclevel_remoney`, `member-levels.iclevel_rebonus` | `R` | 开卡/续充本金和赠送规则候选；新系统卡类没有版本化充值权益规则，必须补模型并确认口径 |
| `member-levels.iclevel_discountC`, `member-levels.iclevel_discountP`, `member-levels.iclevel_discountS`, `member-levels.iclevel_eplanA1`, `member-levels.iclevel_eplanA2`, `member-levels.iclevel_eplanB1`, `member-levels.iclevel_eplanB2`, `member-levels.iclevel_permode`, `member-levels.iclevel_scoremode`, `member-levels.iclevel_useprice` | `R` | 旧折扣、业绩、积分和价格规则；需逐项解释并设计版本化会员权益规则 |
| `member-levels.iclevel_arpu`, `member-levels.iclevel_bonus`, `member-levels.iclevel_memo` | `A` | 当前全空，仅原始归档 |
| `topup-plans.icfull_id` | `D` | 储值方案来源映射键 |
| `topup-plans.icfull_cmoney`, `topup-plans.icfull_zmoney` | `T` | 本金/赠送金额候选，确认后转分；保持两个独立金额 |
| `topup-plans.icfull_iclevel` | `T` | 映射适用会员等级，不能保存显示名外键 |
| `topup-plans.icfull_status` | `T` | 转换为方案启停状态 |
| `topup-plans.icfull_isedit` | `R` | 是否允许编辑/叠加等含义待确认 |
| `topup-plans.icfull_memo` | `E` | 少量自由文本；确认后作为方案说明 |

### 4.8 设施

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `facilities.room_id` | `D` | 设施来源映射键 |
| `facilities.room_code`, `facilities.room_name` | `D` | `facilities.code/display_name` |
| `facilities.room_status` | `T` | 转换为设施生命周期状态，不把“占用中”作为静态配置导入 |
| `facilities.room_bed` | `R` | 可能代表床位数量、房间类型或开关，需确认 |
| `facilities.room_floor`, `facilities.room_memo` | `A` | 当前全空 |

当前设施导出没有门店、服务区和设施类型外键，而新系统这些关系都是必填。9 条设施不能在缺少归属证据时全部挂到默认门店；必须补充按门店读取的来源或由负责人提供门店—服务区—设施映射表。

### 4.9 顾客来源

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `customer-sources.source_id` | `D` | 来源字典映射键 |
| `customer-sources.source_code`, `customer-sources.source_name` | `E` | 建议建立租户级顾客来源字典；`customers.source_code` 引用规范代码 |
| `customer-sources.source_memo` | `A` | 源数据全空 |

### 4.10 顾客、会员卡和权益

| 来源字段 | 等级 | 新系统目标/处理 |
|---|---|---|
| `customers.member_id` | `D` | 顾客来源映射键，生成 `customers.id` |
| `customers.member_name` | `D` | `customers.name`，保留原名 |
| `customers.member_hand` | `T` | 清洗手机号后加密保存并生成查询 HMAC；2287 条非空但只有 2286 个不同值，至少一组重复候选必须人工复核 |
| `customers.member_shop` | `T` | 通过门店代码映射到 `home_store_id`；当前只出现 4 种门店值，而门店表有 5 条，需对账 |
| `customers.member_source` | `T` | 映射顾客来源规范代码 |
| `customers.member_sex` | `T` | 转换为 `CustomerGender` 枚举 |
| `customers.member_birthday` | `T` | 212 条空；其余包含日期、整数、数值和文本多种形态。只接受可证明的日期格式，其他进入异常清单 |
| `customers.member_code` | `R` | 每条唯一但全部呈 HTML 形态；需安全解析并确认它代表会员卡号还是页面链接，确认前不得写 `membership_cards.card_no` |
| `customers.member_iclevel` | `T` | 通过 23 个会员等级映射到 `card_type_id` |
| `customers.member_time1` | `T` | 全量日期时间，作为旧建档时间候选 |
| `customers.member_last`, `customers.member_time2` | `R` | 最近到店/消费/修改时间候选，含空值；需确认含义后决定是否进入活动历史 |
| `customers.member_money`, `customers.member_bonus`, `customers.member_sbonus`, `customers.member_store`, `customers.member_credit`, `customers.member_arrear`, `customers.member_score` | `R` | 余额、本金、赠送金、累计储值、信用/欠款和积分语义不能仅凭字段名确定。没有不可变流水和分账证据前，禁止写入本金、奖励金、积分或欠款账户 |
| `customers.member_discountC`, `customers.member_discountP`, `customers.member_discountS`, `customers.member_dismode` | `R` | 旧顾客折扣快照/模式候选；应优先从会员等级权益规则重建，不能直接覆盖新价格 |
| `customers.member_sms1`, `customers.member_sms2` | `R` | 旧短信标志不能自动等同于现行营销授权；迁移后默认不授权，除非能提供授权时间和来源证据 |
| `customers.member_audit` | `R` | 审核/状态字段含义待确认，不能直接决定顾客停用 |
| `customers.member_memo` | `E` | 自由文本且可能含个人信息；若保留，需加密、字段权限和查看审计 |
| `customers.member_email`, `customers.member_end`, `customers.member_parent` | `A` | 当前全空，仅原始归档 |

## 5. 当前新系统模型缺口

2026-08-20 已通过前向迁移补充通用迁移运行、来源映射、异常和非消费资金快照；其余业务模型缺口仍不通过默认值规避：

1. 已补齐通用迁移运行、来源记录映射、导入版本和异常清单表，并加入非消费资金快照。
2. 缺少租户级顾客来源、员工工种、产品单位等可配置字典。
3. 门店与员工缺少经授权的扩展档案字段；个人字段需要加密和按需查看审计。
4. 缺少会员等级的版本化折扣、积分、开卡和续充权益规则。
5. 缺少可销售的储值方案模板；现有 `member_topup_orders` 只表示已经发生的储值交易。
6. 缺少可销售的次卡商品模板；现有 `member_service_passes` 只表示顾客已经拥有的次数权益。
7. 现有服务提成只支持单一比例或固定金额，不能直接承接旧系统多工种、多档提成字段。
8. 设施导出缺少门店、服务区和设施类型关系。
9. 当前导出只有顾客余额快照，没有储值、消费、退款、积分和次卡流水，不能建立可审计的期初权益。

这些缺口未来如需补齐，只能通过新的 Flyway 前向迁移和独立测试实现；不得手工改正式数据库，也不得为了迁移修改历史迁移文件。

## 6. 进入转换前的阻断清单

| 编号 | 阻断项 | 解除条件 |
|---|---|---|
| MIG-B01 | 顾客资金字段语义不清，缺少流水 | 找到只读充值/消费/退款/账户流水端点，并完成本金、赠送金、积分、欠款对账 |
| MIG-B02 | 设施没有门店/服务区关系 | 获取按店设施数据或负责人确认的映射表 |
| MIG-B03 | 会员等级规则无法无损映射 | 解释全部折扣、充值、积分字段并确认新规则模型 |
| MIG-B04 | 次卡模板缺失 | 确认次数、有效期、适用服务、价格和退款规则后设计模板 |
| MIG-B05 | 储值方案模板缺失 | 确认本金、赠送、适用卡类、有效期和叠加规则 |
| MIG-B06 | 服务/产品价格和提成口径不明 | 由业务负责人确认字段语义、金额单位、提成基数和历史生效时间 |
| MIG-B07 | 顾客手机号存在重复候选 | 输出仅供授权人员查看的重复复核清单并逐组决策，不自动合并 |
| MIG-B08 | 老字段含 HTML | 使用白名单解析器抽取纯文本/标识并做抽样对照，禁止原样渲染 |
| MIG-B09 | 顾客生日格式混杂 | 定义格式优先级；无法证明的值保持空并进入异常清单 |

## 7. 后续工作顺序

1. 继续只读登记账户余额、储值、消费、退款、积分、次卡和库存原始明细接口。
2. 对本矩阵中所有 `R` 项补充业务定义、目标字段、转换函数和验收样例。
3. 设计迁移暂存区、来源映射、异常清单和三类规则模板，但先评审文档，不直接建表。
4. 使用脱敏/合成夹具实现转换器测试，再在隔离测试库中使用真实加密导出数据演练。
5. 完成源数量、目标数量、金额、余额、余次、积分和外键完整性对账后，才申请正式导入批准。

当前结论不授权任何旧系统写操作，也不授权把这些数据写入本地、测试或生产的新 ERP 数据库。
