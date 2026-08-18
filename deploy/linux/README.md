# Ubuntu 24.04 单机发布

本目录为 2 核 4 GB 腾讯云轻量应用服务器提供 Ubuntu Server 24.04 LTS、PostgreSQL 18、ASP.NET Core 10、Nginx、systemd Blue/Green、Flyway、age 加密备份和公网 IP HTTPS 的发布链。它不使用 Docker，不在运行服务器安装 Node.js 或 .NET SDK。

运行时安装依据为 [.NET 官方 Ubuntu 说明](https://learn.microsoft.com/zh-cn/dotnet/core/install/linux-ubuntu-install)，反向代理与服务管理遵循 [ASP.NET Core Linux/Nginx 指南](https://learn.microsoft.com/zh-cn/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-10.0)。Flyway CLI 固定到官方发布页列出的 13.3.0，并在安装前校验仓库内固定的 SHA-256。

## 安全边界

- 公网只开放 TCP 22、80、443；PostgreSQL 与两个 Kestrel 槽位只监听回环地址。
- SSH 只允许 `erpdeploy` 使用公钥登录；禁止 root、密码和键盘交互认证。
- 应用以无登录权限的 `erp` 账号运行；环境密钥位于 `/etc/erp`，发布包不含密钥。
- `/srv/erp/data`、发布目录和 systemd 服务分离；附件与 Data Protection 密钥必须联合备份。
- Flyway 固定 `baselineOnMigrate=false`、`cleanDisabled=true`，只执行校验过的前向迁移。
- HTTPS 使用 Let’s Encrypt 公网 IP 短期证书；Certbot 自动续期并在成功后重载 Nginx。

## 标准流程

1. 在可信构建机更新锁文件并运行：

   ```bash
   ./deploy/linux/Build-Release.sh 1.0.0 ./artifacts/releases
   ```

2. 首次主机初始化从腾讯云 TAT/救援终端以 root 执行：

   ```bash
   ./deploy/linux/Initialize-Host.sh \
     --public-address 203.0.113.10 \
     --ssh-public-key-file /root/erp-deploy.pub
   ```

3. 将 age 接收方公钥写入仅 root 可读的 `/etc/erp/backup.env`：

   ```text
   ERP_BACKUP_AGE_RECIPIENT=age1...
   ERP_BACKUP_DIRECTORY=/srv/erp/backups
   ERP_BACKUP_RETENTION_DAYS=14
   ```

4. 上传 tar.gz 后按包外 SHA-256 发布：

   ```bash
   sudo /usr/local/sbin/erp-deploy /tmp/erp-1.0.0-linux-x64.tar.gz 64位SHA256
   ```

5. 空库首次发布成功后执行一次初始化。初始密码只写入 root 权限文件 `/root/erp-initial-credentials.txt`：

   ```bash
   sudo /usr/local/sbin/erp-bootstrap
   ```

6. 初始化独立平台管理员。凭据只写入 `/root/erp-platform-initial-credentials.txt`，该账号不属于任何商户：

   ```bash
   sudo /usr/local/sbin/erp-platform-bootstrap
   ```

7. 失败时只回退应用槽位，不降级数据库：

   ```bash
   sudo /usr/local/sbin/erp-rollback
   ```

## 隔离恢复演练

`verify-backup.sh` 不安装到生产运维命令，应该在持有 age 私钥的隔离 Linux 恢复机执行。它只接受尚不存在且以 `erp_restore_verify_` 开头的临时数据库，验证摘要、归档路径、逐文件清单、PostgreSQL custom dump 和 schema 后自动删除临时库：

```bash
sudo ERP_RESTORE_CONFIRM=RESTORE_TO_DISPOSABLE_DATABASE \
  ./deploy/linux/verify-backup.sh \
  /secure/erp-backup-20260818-120000.tar.gz.age \
  64位SHA256 \
  /secure/age-recovery.key \
  erp_restore_verify_20260818
```

## 运行目录

```text
/srv/erp/releases/<version>       不可变发布版本
/srv/erp/slots/{blue,green}       槽位软链接
/srv/erp/current                  当前发布软链接
/srv/erp/data/attachments         加密附件
/srv/erp/data/data-protection-keys Cookie 与附件保护密钥
/srv/erp/backups                  age 加密备份
/etc/erp                          root 管理的运行密钥
```

首次成功发布后会启用每日加密备份和 `erp-health.timer`。健康检查每五分钟检查活动服务、HTTPS入口、schema、PostgreSQL、根磁盘、36小时内加密备份和24小时证书余量，失败写入 journald。若需要外部告警，可在仅 root 可读的 `/etc/erp/monitor.env` 配置 HTTPS `ERP_ALERT_WEBHOOK_URL`；没有外部告警接收方时只能算本机监测，不能算告警闭环。

服务器只有一台，Blue/Green 可以减少普通代码发布中断，但不能消除主机、磁盘、网络或 PostgreSQL 单点。加密备份必须定期复制到服务器之外并执行隔离恢复演练。
