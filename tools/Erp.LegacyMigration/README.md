# 旧系统只读迁移工具

该工具只连接 `https://app5.siweicloud.com`，当前只允许读取已逐项登记的顾客及基础资料 jqGrid 列表。除登录请求外，任何 POST、非白名单路径、HTTP 地址、其他主机或带写入语义的动作都会在发出网络请求之前被拒绝。

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

## 验证

```bash
dotnet test tests/unit/Erp.LegacyMigration.Tests/Erp.LegacyMigration.Tests.csproj --configuration Release
```

详细数据范围、后续端点审核和对账规则见 [旧系统数据只读导出与迁移 PRD](../../docs/legacy-system-data-migration-prd.md)，真实字段画像和初步映射见 [旧系统字段画像与初步映射矩阵](../../docs/legacy-system-field-profile-and-mapping.md)。
