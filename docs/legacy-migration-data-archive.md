# 旧系统迁移数据加密归档

迁移数据存放在 GitHub 草稿 Release `legacy-migration-data-baseline`，不直接提交到 Git 历史。仓库为公开仓库，Release 保持草稿状态；仅仓库协作者能够访问草稿资产。

## 归档内容

- `legacy-structured-backup-20260830.tar.gz`：AES-256-GCM 加密迁移归档，包含65个结构化模块、92920条记录和147张加密顾客档案图片。
- `legacy-structured-backup-20260830.tar.gz.sha256`：归档完整性校验文件。
- 护理文字记录包含在结构化模块中；护理图片按产品范围明确排除。
- 解密密钥、旧系统账号密码、验证码、Cookie和任何明文顾客数据均未上传。

归档 SHA-256：

```text
b05b6b2c3da66d948021f08ad2aa990954cc2d1d8c7ec4bdcb91a745ad85c085
```

## 使用规则

1. 下载两个资产后，先运行 `shasum -a 256 -c legacy-structured-backup-20260830.tar.gz.sha256`。
2. 只在受控迁移主机上解压；业务密文仍需迁移工具和本机安全存储中的导出密钥才能解密。
3. 不得把导出密钥写入仓库、Release说明、命令行参数、日志或服务器普通配置文件。
4. 正式导入前必须先在测试品牌执行干跑、主从关联校验和金额对账。
