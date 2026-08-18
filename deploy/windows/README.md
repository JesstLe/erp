# Windows 测试环境发布

本目录实现 GitHub Actions 构建、Flyway 前向迁移、IIS Blue/Green 后端槽位、ARR/URL Rewrite 公共代理切换、`age` 加密备份与隔离恢复。它只面向当前 Windows 测试环境，不等于正式生产已经验收。

## 固定边界

- `ERP-Blue` 与 `ERP-Green` 使用不同的回环端口，默认 `5101/5102`；两者都不直接暴露公网。
- 公共 IIS 站点只承载反向代理目录，公网 HTTPS 请求由 `web.config` 原子切换到活动端口。
- 业务配置、Data Protection 密钥和附件目录在 `releases` 之外；发布包不包含任何密钥。
- Flyway 使用独立迁移账号，`baselineOnMigrate=false`、`cleanDisabled=true`。脚本不执行 `repair` 或数据库降级。
- 应用回退前必须验证目标发布包声明的 schema 兼容范围；回退只切流量，不自动回滚数据库。
- 发布前备份必须经 `age` 公钥加密。只有生成加密文件和 SHA-256 后才删除临时明文。

## 服务器前置条件

安装 PowerShell 7、.NET 10 Hosting Bundle、IIS、URL Rewrite、Application Request Routing、PostgreSQL 18 客户端、Flyway、`age`。所有脚本都从 PowerShell 7 (`pwsh`) 执行。创建公共代理站点及两个最小权限后端站点；应用池身份只读发布目录，并只写外置日志、附件和密钥目录。

至少通过受保护的机器级环境配置提供：

```text
ERP_FLYWAY_URL
ERP_MIGRATOR_USER
ERP_MIGRATOR_PASSWORD
ERP_BACKUP_HOST
ERP_BACKUP_PORT
ERP_BACKUP_DATABASE
ERP_BACKUP_USER
ERP_BACKUP_PASSWORD
```

恢复演练另用 `ERP_RESTORE_*` 管理变量和独立 `age` 身份文件。变量不得写入脚本、发布包、IIS `web.config` 或普通日志。

## 标准顺序

1. CI 运行测试、依赖审计和 `Build-Release.ps1`，生成 zip、SHA-256 和内含逐文件校验的 manifest。
2. 从受信 CI 页面取得 zip 的 SHA-256，通过独立签收记录传给 `Deploy-Erp.ps1 -ExpectedPackageSha256`；脚本不会自动信任与 zip 同目录的哈希文件。
3. 脚本校验磁盘空间与逐文件 SHA-256，生成数据库/附件/密钥联合加密备份。
4. Flyway `validate → migrate → validate`；任何失败都不切换流量。
5. 部署到非活动 IIS 槽位并请求 `/health/ready`；通过后原子更新公共代理。
6. 公共入口再次通过就绪检查后写入 `active-slot.json`。
7. 若应用异常，使用 `Rollback-Erp.ps1` 切回兼容槽位；若数据逻辑异常，停止写入并按事故方案恢复/补偿，不擅自降级 schema。

发布前备份不可跳过。切流后若公共 `/health/ready` 未通过，发布和回退脚本都会恢复原 `web.config`。发布包逐文件 SHA-256 清单必须与实际文件完全一致；多文件、少文件、重复路径或越界路径均拒绝执行。

## 首次环境

新系统从空库开始，直接由 Flyway 执行全部迁移。脚本禁止对已有但没有 `flyway_schema_history` 的非空库自动 baseline；这类数据库必须先做人工盘点、备份、迁移校验值核对和单独变更审批。

## 恢复演练

`Test-Restore.ps1` 只接受名称含 `_restore_` 的新隔离数据库，并拒绝覆盖已有库。恢复后检查核心表存在和会员余额非负；传入 `-DropAfterValidation` 才会删除该隔离库。正式月度演练还要用隔离应用实例抽查附件解密、产品图片与服务档案权限。
