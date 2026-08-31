#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
if [[ -f "$script_directory/common.sh" ]]; then source "$script_directory/common.sh"; else source /usr/local/lib/erp-common.sh; fi
require_root
[[ -L /srv/erp/current && -f /srv/erp/current/app/Erp.Api.dll ]] || die '当前发布版本不存在'
credentials_file=/root/erp-initial-credentials.txt
[[ ! -e "$credentials_file" ]] || die '初始凭据文件已存在，拒绝重复初始化'

tenant_code=${ERP_BOOTSTRAP_TENANT_CODE:-B01}
tenant_name=${ERP_BOOTSTRAP_TENANT_NAME:-门店 ERP}
store_code=${ERP_BOOTSTRAP_STORE_CODE:-S001}
store_name=${ERP_BOOTSTRAP_STORE_NAME:-总店}
owner_account=${ERP_BOOTSTRAP_OWNER_ACCOUNT:-erp.owner}
owner_display_name=${ERP_BOOTSTRAP_OWNER_DISPLAY_NAME:-负责人}
owner_password="A!$(openssl rand -hex 23)"
mapfile -t app_environment < <(grep -Ev '^[[:space:]]*(#|$)' /etc/erp/erp.env)

runuser -u erp -- env "${app_environment[@]}" \
  ERP_BOOTSTRAP_CONFIRM=CREATE_NEW_ERP \
  ERP_BOOTSTRAP_TENANT_CODE="$tenant_code" \
  ERP_BOOTSTRAP_TENANT_NAME="$tenant_name" \
  ERP_BOOTSTRAP_STORE_CODE="$store_code" \
  ERP_BOOTSTRAP_STORE_NAME="$store_name" \
  ERP_BOOTSTRAP_STORE_TIME_ZONE=Asia/Shanghai \
  ERP_BOOTSTRAP_OWNER_ACCOUNT="$owner_account" \
  ERP_BOOTSTRAP_OWNER_DISPLAY_NAME="$owner_display_name" \
  ERP_BOOTSTRAP_OWNER_EMPLOYEE_NO=E0001 \
  ERP_BOOTSTRAP_OWNER_POSITION=负责人 \
  ERP_BOOTSTRAP_OWNER_PASSWORD="$owner_password" \
  /usr/bin/dotnet /srv/erp/current/app/Erp.Api.dll --bootstrap

cat >"$credentials_file" <<EOF
ERP 地址: $(. /etc/erp/host.env; printf '%s' "$ERP_PUBLIC_READY_URL" | sed 's#/health/ready##')
初始账号: $owner_account
初始密码: $owner_password
要求: 首次登录后立即修改密码
EOF
chmod 0600 "$credentials_file"
printf 'BOOTSTRAPPED:%s:%s\n' "$tenant_code" "$owner_account"
