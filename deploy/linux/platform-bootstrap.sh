#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi
require_root
[[ -L /srv/erp/current && -f /srv/erp/current/app/Erp.Api.dll ]] || die '当前发布版本不存在'
credentials_file=/root/erp-platform-initial-credentials.txt
[[ ! -e "$credentials_file" ]] || die '平台初始凭据文件已存在，拒绝重复初始化'

platform_account=${ERP_PLATFORM_ADMIN_ACCOUNT:-platform.admin}
platform_display_name=${ERP_PLATFORM_ADMIN_DISPLAY_NAME:-平台管理员}
platform_password="P!$(openssl rand -hex 23)"
mapfile -t app_environment < <(grep -Ev '^[[:space:]]*(#|$)' /etc/erp/erp.env)

runuser -u erp -- env "${app_environment[@]}" \
  ERP_PLATFORM_BOOTSTRAP_CONFIRM=CREATE_PLATFORM_ADMIN \
  ERP_PLATFORM_ADMIN_ACCOUNT="$platform_account" \
  ERP_PLATFORM_ADMIN_DISPLAY_NAME="$platform_display_name" \
  ERP_PLATFORM_ADMIN_PASSWORD="$platform_password" \
  /usr/bin/dotnet /srv/erp/current/app/Erp.Api.dll --bootstrap-platform-admin

cat >"$credentials_file" <<EOF
ERP 平台地址: $(. /etc/erp/host.env; printf '%s' "$ERP_PUBLIC_READY_URL" | sed 's#/health/ready##')/platform/login
平台初始账号: $platform_account
平台初始密码: $platform_password
要求: 首次登录后立即修改密码；不得与任何商户 OWNER 共用
EOF
chmod 0600 "$credentials_file"
printf 'PLATFORM_BOOTSTRAPPED:%s\n' "$platform_account"
