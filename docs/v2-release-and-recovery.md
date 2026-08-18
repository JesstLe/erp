# V2 发布、备份与恢复验收说明

更新日期：2026-08-18

## 目标与完成口径

V2-07 解决“代码更新不能直接覆盖运行目录、数据库变更不能把系统改崩、失败后能明确回到哪里”的问题。发布以 Git 提交和不可变版本包为输入，数据库只做前向迁移，应用使用Linux systemd Blue/Green切换；备份和恢复是独立安全链，不把数据库降级伪装成普通回滚。

## 发布链

1. GitHub Actions 使用锁文件恢复依赖，执行后端/前端测试及 NuGet、npm 漏洞检查。
2. `Build-Release.sh` 发布 `linux-x64` 框架依赖型 API，并将 React 构建产物放入 API 的 `wwwroot`。
3. 发布包清单记录版本、Git SHA、构建时间、运行时、schema 最小/最大值及每个文件的 SHA-256；包外另有 tar.gz SHA-256。
4. `Deploy-Release.sh` 要求从受信 CI 签收记录显式传入包 SHA-256，校验归档路径和全部文件，强制生成发布前加密备份，再执行 Flyway 校验和前向迁移。
5. 新版本只部署到非活动 systemd 槽位，先请求回环 `/health/ready`；通过后原子切换 Nginx upstream，再次验证 HTTPS 入口。
6. 公网验证失败立即恢复原代理配置；只有两级健康检查都成功才更新 `active-slot.json`。

## 数据库安全边界

- `db/migrations` 是唯一结构来源；共享环境禁止手工改表和 EF 自动迁移。
- Flyway 明确设置 `baselineOnMigrate=false`、`cleanDisabled=true`，脚本不调用 `repair`。
- 新系统从空库开始。已有非空库若没有 Flyway 历史，必须人工盘点，不能自动 baseline。
- `/health/ready` 同时检查数据库可连接、关键业务表、实时检索索引以及V25预约/班次表存在，并返回应用要求的 schema 版本。
- 应用回退从当前活动记录读取 schema，再检查目标版本兼容范围；不接受操作员手工声称的版本，不执行 SQL 降级。

## 备份与恢复链

- 发布前通过 `pg_dump --format=custom` 备份数据库，可联合外置附件和 Data Protection 密钥目录。
- 每个内容文件记录大小和 SHA-256，再压缩并用 `age` 接收方公钥加密；加密成功后删除临时明文和工作目录。
- 加密备份的私钥不放在服务器发布目录、仓库、脚本或日志中，备份文件必须传出当前服务器。
- `verify-backup.sh` 只接受名称以 `erp_restore_verify_` 开头的全新数据库，先拒绝已存在目标，再解密、验证清单、恢复并检查schema，最后删除临时数据库。
- 联合恢复还必须用隔离应用抽查产品图片和服务档案图片可解密、摘要一致、授权范围正确；脚本的数据库校验不能替代这一步。

## 已验证证据与未验证边界

本机已完成锁定恢复、全部测试、API就绪探针、`linux-x64`框架依赖发布包、内嵌前端产物、逐文件清单和包外摘要验证。历史开发库已完成PostgreSQL custom dump隔离恢复。Apple Silicon只验证Linux x64构建和文件结构，不算运行验收；仓库测试锁定清单完整性、路径边界、迁移禁令、代理恢复、schema兼容、加密与隔离恢复规则。

当前开发机是macOS，不能模拟systemd、UFW、Nginx公网切流和证书签发，所以以下项目仍是目标Linux验收项：

- Ubuntu 24.04完成新机初始化、SSH公钥登录和入站端口核查。
- systemd两个回环槽位与Nginx完成正常切流、故障恢复和应用回退。
- `age` 真实密钥完成数据库、附件、Data Protection 密钥联合备份与隔离恢复。
- Linux服务账号、目录权限、公网IP HTTPS及续期、外部备份传输和监控告警通过运维检查。

未完成这些真机步骤前，可称“V2-07 Linux仓库自动化闭环”，不可称“Linux生产部署已验收”。
