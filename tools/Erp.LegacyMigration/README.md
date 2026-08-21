# 旧系统只读迁移工具

该工具只连接 `https://app5.siweicloud.com`。读取端点逐项登记，包括顾客与基础资料 jqGrid、顾客护理列表、顾客/护理详情照片索引页和两个精确图片目录。除登录和护理页取得 jqGrid 列模型所必需的空 `POST ...?act=custom` 外，任何 POST、非白名单路径、HTTP 地址、其他主机或带写入语义的动作都会在发出网络请求之前被拒绝；这个旧式 `custom` POST 只读取表格结构，不提交业务字段。

## 运行前准备

必须提供以下敏感值：

- `ERP_LEGACY_ACCOUNT`：旧系统账号。
- `ERP_LEGACY_PASSWORD`：旧系统密码。
- `ERP_LEGACY_EXPORT_KEY`：32 字节随机密钥的 Base64 文本，用于 AES-256-GCM 加密导出内容。

可以通过安全环境变量提供，也可以在交互终端按提示输入；账号、密码和加密密钥交互输入时均不回显。不要把真实值写进 `.env.example`、命令参数、脚本或提交记录。

输出目录必须使用绝对路径并位于 Git 工作区之外：

```bash
dotnet run --project tools/Erp.LegacyMigration -- \
  export \
  --entity customers \
  --output /安全目录/legacy-export-20260819
```

基础资料端点逐项登记后，可以在同一登录会话中批量导出：

```bash
dotnet run --project tools/Erp.LegacyMigration -- \
  export \
  --entity base-master \
  --output /安全目录/legacy-base-master-20260819
```

`base-master` 当前包含门店、员工、服务项目、产品、次卡目录、会员卡类、储值方案、设施、品牌、单位、员工工种和来店渠道，不包含已经单独导出的顾客数据。

护理列表、顾客档案照片和护理记录照片需要基于同一次顾客导出运行 `extras`。工具逐份读取顾客和护理详情，每份最多识别两张照片；照片原始字节在落盘前使用同一 AES-256-GCM 密钥加密：

```bash
dotnet run --project tools/Erp.LegacyMigration -- \
  extras \
  --input /安全目录/legacy-customers \
  --output /安全目录/legacy-extras
```

护理列表查询必须携带旧界面的 `search_find=Y`、日期、门店、分类和会员筛选参数；缺少总开关时旧接口会静默返回空数据。`extras` 只允许经审核的护理列表/列模型请求、带数字主键的顾客或护理详情读取，以及 `/swshop/picture/<安全目录>/{member|nurse}/<安全文件名>` 下的 JPEG、PNG、WebP。页面图标、路径穿越、其他目录和其他文件类型均被拒绝。

详情与图片正文设置300秒边界，以容纳旧站低带宽下接近5MB的合法图片；超时、TLS提前断开或暂时网络失败的主键写入检查点失败清单，后续新会话优先重试，不能把失败静默计成“无照片”。顾客详情保持单请求串行；护理详情串行发现图片地址，再以限流并发下载静态图片。所有阶段均可从已验证检查点续跑。

工具会把当次验证码保存到输出目录并暂停；操作者读取图片后输入四位数字。登录成功后验证码文件立即删除。

## 输出

```text
customers/
├── checkpoint.json       # 无业务数据；用于断点恢复
├── manifest.json         # 无业务数据；记录条数、端点和校验值
├── page-000001.json.enc  # 加密的原始 jqGrid 响应
└── rows.jsonl.enc        # 加密的逐行记录
```

加密文件使用 `ERPLEG1` 格式和 AES-256-GCM。遗失导出密钥后无法恢复数据；密钥错误或文件被篡改时工具会停止。

在 Unix 系统上，顶层与模块目录强制为 `0700`，所有检查点、清单和密文文件强制为 `0600`。清单和检查点只包含端点标识、页码、数量、时间与摘要，不包含业务字段值。

重复使用相同输出目录和相同分页参数会校验已完成页面并从检查点继续。页文件缺失、摘要变化、响应变成登录页、JSON 结构变化或页码不一致时不会跳过错误。

## 离线字段画像

字段画像不连接旧系统，只读取已完成的加密导出。可以同时提供多个导出根目录：

```bash
dotnet run --project tools/Erp.LegacyMigration -- \
  profile \
  --input /安全目录/legacy-customers \
  --input /安全目录/legacy-base-master \
  --output /安全目录/legacy-profile/field-profile.json
```

画像会重新校验清单、端点登记、记录数和密文 SHA-256，再在内存解密逐行文件。输出只包含字段名、类型、出现/空值/去重数量、最大长度、候选键、格式类别和敏感级别；不包含任何来源值、值摘要、输入路径、账号、Cookie 或密钥。路径穿越、符号链接、重复 JSON 字段、超大文件、密文篡改或数量不一致都会停止处理。

## 受控导入测试品牌

导入与只读导出是两个独立阶段。当前实现被代码双重限制为测试品牌 `B01`，默认只做事务干跑；只有显式增加 `--apply` 才会提交：

```bash
# 干跑：完整转换、约束检查和计数，最后回滚
dotnet Erp.LegacyMigration.dll import \
  --tenant B01 \
  --input /安全目录/legacy-customers \
  --input /安全目录/legacy-base-master \
  --input /安全目录/legacy-extras

# 仅在备份、干跑与对账均通过后执行
dotnet Erp.LegacyMigration.dll import \
  --tenant B01 \
  --input /安全目录/legacy-customers \
  --input /安全目录/legacy-base-master \
  --input /安全目录/legacy-extras \
  --apply
```

导入按来源实体、来源主键和SHA-256建立幂等映射。旧门店使用新系统自动门店编码，并同时登记旧主键、代码和名称别名；员工不自动创建登录账号；服务、产品和会员卡类使用`LEGACY-*`技术编码但保留显示名称；顾客手机号使用生产环境原有Data Protection和查询HMAC加密。顾客备注/照片与护理记录/护理照片分别形成明确标记为旧系统迁移的服务档案。护理记录按`bill_member`关联顾客、按`bill_shop`唯一映射门店，无法映射的非空门店值会中止事务，不再回退第一家门店。

旧系统余额、赠送、累计储值、欠款和积分在缺少不可变流水及字段口径证明时，只进入 `legacy_customer_financial_snapshots` 非消费快照，数据库约束固定 `is_spendable=false`，绝不写入会员可用本金、奖励金或积分账户。无法证明的生日和格式异常进入迁移异常表，不用默认值猜测。

发布包把工具放在 `ops/legacy-migration`，不暴露 HTTP 迁移接口。服务器只允许管理员通过 SSH 在备份后运行，并从 `/etc/erp/erp.env` 读取新系统数据库、密钥环和隐私配置；旧系统账号、密码和导出密钥仍通过临时安全环境提供，不进入发布包。

## 验证

```bash
dotnet test tests/unit/Erp.LegacyMigration.Tests/Erp.LegacyMigration.Tests.csproj --configuration Release
```

详细数据范围、后续端点审核和对账规则见 [旧系统数据只读导出与迁移 PRD](../../docs/legacy-system-data-migration-prd.md)，真实字段画像和初步映射见 [旧系统字段画像与初步映射矩阵](../../docs/legacy-system-field-profile-and-mapping.md)。
