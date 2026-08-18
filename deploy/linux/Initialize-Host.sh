#!/usr/bin/env bash
set -Eeuo pipefail
umask 027

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=common.sh
source "$script_directory/common.sh"

flyway_version='13.3.0'
flyway_sha256='329bce15655eda5507ca134fd2b98c1dafbd432af85fd2cce0c8bd2453b613ac'
postgresql_repository_url=${ERP_PGDG_REPOSITORY_URL:-https://apt.postgresql.org/pub/repos/apt}
public_address=''
ssh_public_key_file=''

usage() {
  printf '用法: %s --public-address IPV4 --ssh-public-key-file PATH\n' "$0" >&2
  exit 2
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --public-address) public_address=${2:-}; shift 2 ;;
    --ssh-public-key-file) ssh_public_key_file=${2:-}; shift 2 ;;
    *) usage ;;
  esac
done

require_root
[[ "$public_address" =~ ^([0-9]{1,3}\.){3}[0-9]{1,3}$ ]] || die '公网 IPv4 格式无效'
[[ -f "$ssh_public_key_file" ]] || die 'SSH 公钥文件不存在'
grep -Eq '^ssh-(ed25519|rsa) ' "$ssh_public_key_file" || die '只接受 OpenSSH 公钥文件'

# shellcheck disable=SC1091
source /etc/os-release
[[ ${ID:-} == ubuntu && ${VERSION_ID:-} == 24.04 ]] || die '只支持 Ubuntu Server 24.04 LTS'
[[ $(uname -m) == x86_64 ]] || die '只支持 x86_64 主机'

export DEBIAN_FRONTEND=noninteractive
log '安装系统基础组件'
apt-get update
apt-get install -y ca-certificates curl gnupg jq openssl python3 python3-venv nginx age openssh-server ufw postgresql-common

log '配置 PostgreSQL 官方 Apt 仓库并安装 PostgreSQL 18'
install -d -m 0755 /usr/share/postgresql-common/pgdg
curl --fail --silent --show-error --location \
  --output /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc \
  https://www.postgresql.org/media/keys/ACCC4CF8.asc
cat >/etc/apt/sources.list.d/pgdg.sources <<EOF
Types: deb
URIs: $postgresql_repository_url
Suites: noble-pgdg
Components: main
Signed-By: /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc
EOF
apt-get update
apt-get install -y postgresql-18 postgresql-client-18 aspnetcore-runtime-10.0

log '安装并校验 Flyway CLI'
flyway_archive=$(mktemp /tmp/flyway.XXXXXX.tar.gz)
if [[ -n ${ERP_FLYWAY_ARCHIVE:-} ]]; then
  cp "$ERP_FLYWAY_ARCHIVE" "$flyway_archive"
else
  curl --fail --silent --show-error --location --retry 3 \
    --output "$flyway_archive" \
    "https://download.red-gate.com/maven/release/com/redgate/flyway/flyway-commandline/$flyway_version/flyway-commandline-$flyway_version-linux-x64.tar.gz"
fi
[[ $(sha256_file "$flyway_archive") == "$flyway_sha256" ]] || die 'Flyway SHA-256 校验失败'
rm -rf "/opt/flyway-$flyway_version"
tar -xzf "$flyway_archive" -C /opt
rm -f "$flyway_archive"
ln -sfn "/opt/flyway-$flyway_version/flyway" /usr/local/bin/flyway
flyway --version >/dev/null

log '安装支持 IP 证书的 Certbot'
python3 -m venv /opt/certbot
/opt/certbot/bin/pip install --disable-pip-version-check --no-cache-dir 'certbot==5.7.0'
ln -sfn /opt/certbot/bin/certbot /usr/local/bin/certbot
certbot --version | grep -Eq 'certbot (5\.[4-9]|[6-9]\.|[1-9][0-9]\.)' || die 'Certbot 版本不支持 IP 证书'

log '创建最小权限运行账号和持久化目录'
id -u erp >/dev/null 2>&1 || useradd --system --home /srv/erp --shell /usr/sbin/nologin erp
id -u erpdeploy >/dev/null 2>&1 || useradd --create-home --shell /bin/bash erpdeploy
install -d -m 0750 -o root -g erp /srv/erp /srv/erp/releases /srv/erp/slots /srv/erp/logs
install -d -m 0750 -o erp -g erp /srv/erp/data /srv/erp/data/attachments /srv/erp/data/data-protection-keys
install -d -m 0700 -o root -g root /srv/erp/backups /etc/erp
install -d -m 0755 -o root -g root /var/www/erp-acme

log '配置专用 SSH 密钥入口'
install -d -m 0700 -o erpdeploy -g erpdeploy /home/erpdeploy/.ssh
install -m 0600 -o erpdeploy -g erpdeploy "$ssh_public_key_file" /home/erpdeploy/.ssh/authorized_keys
cat >/etc/ssh/sshd_config.d/90-erp-hardening.conf <<'EOF'
PubkeyAuthentication yes
PasswordAuthentication no
KbdInteractiveAuthentication no
PermitRootLogin no
AllowUsers erpdeploy
EOF
sshd -t
systemctl enable --now ssh
systemctl restart ssh

cat >/etc/sudoers.d/erpdeploy <<'EOF'
erpdeploy ALL=(root) NOPASSWD: /usr/local/sbin/erp-deploy, /usr/local/sbin/erp-rollback, /usr/local/sbin/erp-bootstrap, /usr/local/sbin/erp-platform-bootstrap, /usr/local/sbin/erp-backup
EOF
chmod 0440 /etc/sudoers.d/erpdeploy
visudo -cf /etc/sudoers.d/erpdeploy >/dev/null

log '限制主机入站端口'
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp comment 'SSH'
ufw allow 80/tcp comment 'ACME and HTTPS redirect'
ufw allow 443/tcp comment 'ERP HTTPS'
ufw --force enable

log '配置 PostgreSQL 本机监听与独立账号'
install -d -m 0755 /etc/postgresql/18/main/conf.d
cat >/etc/postgresql/18/main/conf.d/erp.conf <<'EOF'
listen_addresses = '127.0.0.1,::1'
password_encryption = 'scram-sha-256'
EOF
chmod 0644 /etc/postgresql/18/main/conf.d/erp.conf
systemctl restart postgresql@18-main

app_password=$(openssl rand -hex 32)
migrator_password=$(openssl rand -hex 32)
backup_password=$(openssl rand -hex 32)
sudo -u postgres psql -v ON_ERROR_STOP=1 \
  --set=app_password="$app_password" \
  --set=migrator_password="$migrator_password" \
  --set=backup_password="$backup_password" <<'SQL'
SELECT format('CREATE ROLE erp_migrator LOGIN PASSWORD %L', :'migrator_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_migrator') \gexec
SELECT format('CREATE ROLE erp_app LOGIN PASSWORD %L', :'app_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_app') \gexec
SELECT format('CREATE ROLE erp_backup LOGIN PASSWORD %L', :'backup_password')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'erp_backup') \gexec
SELECT format('ALTER ROLE erp_migrator PASSWORD %L', :'migrator_password') \gexec
SELECT format('ALTER ROLE erp_app PASSWORD %L', :'app_password') \gexec
SELECT format('ALTER ROLE erp_backup PASSWORD %L', :'backup_password') \gexec
SQL
if ! sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='erp'" | grep -qx 1; then
  sudo -u postgres createdb --owner=erp_migrator --encoding=UTF8 erp
fi
sudo -u postgres psql -v ON_ERROR_STOP=1 -d erp <<'SQL'
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT CONNECT ON DATABASE erp TO erp_app, erp_backup;
GRANT USAGE ON SCHEMA public TO erp_app, erp_backup;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_migrator IN SCHEMA public
  GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_migrator IN SCHEMA public
  GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_migrator IN SCHEMA public
  GRANT SELECT ON TABLES TO erp_backup;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_migrator IN SCHEMA public
  GRANT SELECT ON SEQUENCES TO erp_backup;
SQL

privacy_pepper=$(openssl rand -hex 48)
verification_pepper=$(openssl rand -hex 48)
security_event_pepper=$(openssl rand -hex 48)
registration_contact_pepper=$(openssl rand -hex 48)
cat >/etc/erp/erp.env <<EOF
ASPNETCORE_ENVIRONMENT=Production
AllowedHosts=$public_address;127.0.0.1;localhost
ConnectionStrings__ErpDatabase=Host=127.0.0.1;Port=5432;Database=erp;Username=erp_app;Password=$app_password
CustomerPrivacy__LookupPepper=$privacy_pepper
MemberVerification__CodePepper=$verification_pepper
SecurityEvents__AccountHashPepper=$security_event_pepper
PlatformRegistration__ContactHashPepper=$registration_contact_pepper
FileStorage__RootPath=/srv/erp/data/attachments
DataProtection__KeyRingPath=/srv/erp/data/data-protection-keys
EOF
chown root:erp /etc/erp/erp.env
chmod 0640 /etc/erp/erp.env
cat >/etc/erp/migrator.env <<EOF
ERP_FLYWAY_URL=jdbc:postgresql://127.0.0.1:5432/erp
ERP_MIGRATOR_USER=erp_migrator
ERP_MIGRATOR_PASSWORD=$migrator_password
EOF
chmod 0600 /etc/erp/migrator.env
cat >/etc/erp/backup-db.env <<EOF
ERP_BACKUP_HOST=127.0.0.1
ERP_BACKUP_PORT=5432
ERP_BACKUP_DATABASE=erp
ERP_BACKUP_USER=erp_backup
ERP_BACKUP_PASSWORD=$backup_password
EOF
chmod 0600 /etc/erp/backup-db.env
cat >/etc/erp/host.env <<EOF
ERP_PUBLIC_ADDRESS=$public_address
ERP_PUBLIC_READY_URL=https://$public_address/health/ready
EOF
chmod 0644 /etc/erp/host.env

log '安装 systemd Blue/Green 服务'
for slot in blue green; do
  port=5101; [[ "$slot" == green ]] && port=5102
  cat >"/etc/systemd/system/erp-$slot.service" <<EOF
[Unit]
Description=ERP $slot slot
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
Type=simple
User=erp
Group=erp
WorkingDirectory=/srv/erp/slots/$slot/app
EnvironmentFile=/etc/erp/erp.env
Environment=ASPNETCORE_URLS=http://127.0.0.1:$port
ExecStart=/usr/bin/dotnet /srv/erp/slots/$slot/app/Erp.Api.dll
Restart=always
RestartSec=5
TimeoutStopSec=30
KillSignal=SIGINT
NoNewPrivileges=true
PrivateTmp=true
ProtectHome=true
ProtectSystem=strict
ReadWritePaths=/srv/erp/data /srv/erp/logs

[Install]
WantedBy=multi-user.target
EOF
done
systemctl daemon-reload

log '配置 Nginx ACME 入口并申请公网 IP 短期证书'
cat >/etc/nginx/sites-available/erp-http <<EOF
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name $public_address;
    location ^~ /.well-known/acme-challenge/ { root /var/www/erp-acme; }
    location / { return 302 https://\$host\$request_uri; }
}
EOF
rm -f /etc/nginx/sites-enabled/default
ln -sfn /etc/nginx/sites-available/erp-http /etc/nginx/sites-enabled/erp
nginx -t
systemctl enable --now nginx
systemctl reload nginx
certbot certonly --non-interactive --agree-tos --register-unsafely-without-email \
  --preferred-profile shortlived --webroot --webroot-path /var/www/erp-acme \
  --cert-name erp-ip --ip-address "$public_address"

cat >/etc/nginx/snippets/erp-upstream.conf <<'EOF'
proxy_pass http://127.0.0.1:5101;
EOF
cat >/etc/nginx/sites-available/erp <<EOF
server {
    listen 80 default_server;
    listen [::]:80 default_server;
    server_name $public_address;
    location ^~ /.well-known/acme-challenge/ { root /var/www/erp-acme; }
    location / { return 308 https://\$host\$request_uri; }
}
server {
    listen 443 ssl http2 default_server;
    listen [::]:443 ssl http2 default_server;
    server_name $public_address;
    ssl_certificate /etc/letsencrypt/live/erp-ip/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/erp-ip/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    client_max_body_size 6m;
    location / {
        include /etc/nginx/snippets/erp-upstream.conf;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Forwarded-Host \$host;
        proxy_set_header X-Forwarded-Proto https;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_read_timeout 120s;
    }
}
EOF
ln -sfn /etc/nginx/sites-available/erp /etc/nginx/sites-enabled/erp
install -d -m 0755 /etc/letsencrypt/renewal-hooks/deploy
cat >/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh <<'EOF'
#!/usr/bin/env bash
set -Eeuo pipefail
nginx -t
systemctl reload nginx
EOF
chmod 0755 /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
nginx -t
systemctl reload nginx

log '安装受控运维命令'
install -m 0755 "$script_directory/Deploy-Release.sh" /usr/local/sbin/erp-deploy
install -m 0755 "$script_directory/rollback.sh" /usr/local/sbin/erp-rollback
install -m 0755 "$script_directory/bootstrap.sh" /usr/local/sbin/erp-bootstrap
install -m 0755 "$script_directory/platform-bootstrap.sh" /usr/local/sbin/erp-platform-bootstrap
install -m 0755 "$script_directory/backup.sh" /usr/local/sbin/erp-backup
install -m 0755 "$script_directory/health-check.sh" /usr/local/sbin/erp-health-check
install -m 0644 "$script_directory/common.sh" /usr/local/lib/erp-common.sh
install -m 0644 "$script_directory/erp-health.service" /etc/systemd/system/erp-health.service
install -m 0644 "$script_directory/erp-health.timer" /etc/systemd/system/erp-health.timer
install -m 0644 "$script_directory/erp-backup.service" /etc/systemd/system/erp-backup.service
install -m 0644 "$script_directory/erp-backup.timer" /etc/systemd/system/erp-backup.timer
cat >/etc/systemd/system/certbot-renew.service <<'EOF'
[Unit]
Description=Renew Let's Encrypt certificates

[Service]
Type=oneshot
ExecStart=/usr/local/bin/certbot renew --quiet
EOF
cat >/etc/systemd/system/certbot-renew.timer <<'EOF'
[Unit]
Description=Run Certbot renewal twice daily

[Timer]
OnCalendar=*-*-* 00,12:00:00
RandomizedDelaySec=4h
Persistent=true

[Install]
WantedBy=timers.target
EOF
systemctl daemon-reload
systemctl enable --now certbot-renew.timer

printf 'HOST_INITIALIZED:%s\n' "$public_address"
