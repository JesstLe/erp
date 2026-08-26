#!/usr/bin/env bash
set -Eeuo pipefail
umask 022

script_directory=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repository_root=$(cd -- "$script_directory/../.." && pwd)
# shellcheck source=common.sh
source "$script_directory/common.sh"

usage() {
  printf '用法: %s VERSION OUTPUT_DIRECTORY\n' "$0" >&2
  exit 2
}

[[ $# -eq 2 ]] || usage
version=$1
output_directory=$2
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]] || die '版本号格式无效'
mkdir -p "$output_directory"
output_directory=$(cd -- "$output_directory" && pwd)

for command_name in dotnet npm git python3 tar; do require_command "$command_name"; done

git_commit=$(git -C "$repository_root" rev-parse HEAD)
[[ "$git_commit" =~ ^[0-9a-f]{40}$ ]] || die '无法取得 Git 提交标识'
if [[ -n $(git -C "$repository_root" status --porcelain --untracked-files=all) ]]; then
  worktree_dirty=true
else
  worktree_dirty=false
fi

schema_version=$(find "$repository_root/db/migrations" -maxdepth 1 -type f -name 'V*.sql' -print |
  sed -E 's#^.*/V([0-9]+)__.*#\1#' | sort -n | tail -1)
[[ "$schema_version" =~ ^[0-9]+$ ]] || die '无法识别 schema 版本'

source_fingerprint=$(
  while IFS= read -r -d '' source_path; do
    [[ -f "$repository_root/$source_path" ]] || continue
    printf '%s\0%s\n' "$source_path" "$(sha256_file "$repository_root/$source_path")"
  done < <(git -C "$repository_root" ls-files --cached --others --exclude-standard -z) |
    { if command -v sha256sum >/dev/null 2>&1; then sha256sum; else shasum -a 256; fi; } |
    awk '{print $1}'
)

staging_directory=$(mktemp -d "${TMPDIR:-/tmp}/erp-release.XXXXXX")
cleanup() { rm -rf -- "$staging_directory"; }
trap cleanup EXIT
app_directory="$staging_directory/app"

log '验证单元测试并发布可在 Linux x64 运行的框架依赖 API'
dotnet restore "$repository_root/tests/unit/Erp.Domain.Tests/Erp.Domain.Tests.csproj" --locked-mode
dotnet test "$repository_root/tests/unit/Erp.Domain.Tests/Erp.Domain.Tests.csproj" -c Release --no-restore
dotnet restore "$repository_root/apps/api/Erp.Api/Erp.Api.csproj" --locked-mode
dotnet publish "$repository_root/apps/api/Erp.Api/Erp.Api.csproj" -c Release \
  --no-restore --runtime linux-x64 --self-contained false -p:UseAppHost=false \
  -p:CopyLocalLockFileAssemblies=true -p:Version="$version" \
  -p:InformationalVersion="$version+$git_commit" -o "$app_directory"

for required_assembly in Erp.Api.dll Microsoft.EntityFrameworkCore.dll Npgsql.dll; do
  [[ -f "$app_directory/$required_assembly" ]] || die "API 发布缺少关键程序集: $required_assembly"
done

log '发布仅供服务器管理员运行的旧系统迁移工具'
legacy_tool_directory="$staging_directory/ops/legacy-migration"
dotnet restore "$repository_root/tools/Erp.LegacyMigration/Erp.LegacyMigration.csproj" --locked-mode
dotnet publish "$repository_root/tools/Erp.LegacyMigration/Erp.LegacyMigration.csproj" -c Release \
  --no-restore --runtime linux-x64 --self-contained false -p:UseAppHost=false \
  -p:CopyLocalLockFileAssemblies=true -p:Version="$version" \
  -p:InformationalVersion="$version+$git_commit" -o "$legacy_tool_directory"

log '审计、测试并构建 React 前端'
(
  cd "$repository_root/apps/web"
  npm ci
  npm run lint
  npm test
  npm audit --audit-level=high
  VITE_APP_VERSION="$version" VITE_APP_ENVIRONMENT=Production npm run build
)
mkdir -p "$app_directory/wwwroot"
cp -R "$repository_root/apps/web/dist/." "$app_directory/wwwroot/"
cp -R "$repository_root/db" "$staging_directory/db"
mkdir -p "$staging_directory/deploy"
mkdir -p "$staging_directory/deploy/linux"
for deployment_file in common.sh Initialize-Host.sh Deploy-Release.sh backup.sh bootstrap.sh platform-bootstrap.sh rollback.sh README.md; do
  cp "$script_directory/$deployment_file" "$staging_directory/deploy/linux/$deployment_file"
done

python3 - "$staging_directory" "$version" "$git_commit" "$worktree_dirty" "$source_fingerprint" "$schema_version" <<'PY'
import datetime, hashlib, json, os, sys
root, version, commit, dirty, source_hash, schema = sys.argv[1:]
files = []
for base, _, names in os.walk(root):
    for name in sorted(names):
        full = os.path.join(base, name)
        relative = os.path.relpath(full, root).replace(os.sep, "/")
        if relative == "release-manifest.json":
            continue
        digest = hashlib.sha256()
        with open(full, "rb") as handle:
            for chunk in iter(lambda: handle.read(1024 * 1024), b""):
                digest.update(chunk)
        files.append({"path": relative, "size": os.path.getsize(full), "sha256": digest.hexdigest()})
manifest = {
    "formatVersion": 1,
    "version": version,
    "gitCommit": commit,
    "worktreeDirty": dirty == "true",
    "sourceFingerprintSha256": source_hash,
    "builtAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
    "runtime": "linux-x64-framework-dependent",
    "schema": {"min": schema, "max": schema},
    "files": sorted(files, key=lambda item: item["path"]),
}
with open(os.path.join(root, "release-manifest.json"), "w", encoding="utf-8") as handle:
    json.dump(manifest, handle, ensure_ascii=False, indent=2)
    handle.write("\n")
PY

package="$output_directory/erp-$version-linux-x64.tar.gz"
COPYFILE_DISABLE=1 tar -C "$staging_directory" -czf "$package" .
package_hash=$(sha256_file "$package")
printf '%s  %s\n' "$package_hash" "$(basename "$package")" >"$package.sha256"
printf '%s\n' "$package"
