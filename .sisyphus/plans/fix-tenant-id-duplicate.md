# 修复计划：CashierServiceTimer tenant_id 重复问题

## 问题分析

**错误信息：**
```
java.sql.SQLSyntaxErrorException: Column 'tenant_id' specified twice
SQL: INSERT INTO jsh_cashier_service_timer (session_id, status, duration_seconds, start_time, end_time, tenant_id, delete_flag, tenant_id) VALUES (?, ?, ?, ?, ?, ?, ?, 63)
```

**根本原因：**
1. 项目使用 MyBatis-Plus 的多租户插件 `TenantSqlParser`（配置在 `TenantConfig.java` 中）
2. 该插件会自动在 INSERT 语句中注入 `tenant_id` 列和值
3. 但在 `CashierServiceTimerService.java` 中，第70行手动设置了 `timer.setTenantId(tenantId)`
4. 这导致 `tenant_id` 被添加了两次：
   - 一次由 MyBatis-Plus 插件自动注入
   - 一次由 MyBatis 根据 `tenantId != null` 条件生成

**修复方案：**
在插入操作前，移除手动设置 `tenantId` 的代码，让 MyBatis-Plus 插件自动处理多租户字段。

## 需要修改的文件

### 文件1: CashierServiceTimerService.java
**路径：** `erp-boot/src/main/java/com/jsh/erp/service/cashier/CashierServiceTimerService.java`

**修改内容（第63-72行）：**

删除第70行的 `timer.setTenantId(tenantId);`

**修改前：**
```java
CashierServiceTimer timer = new CashierServiceTimer();
timer.setSessionId(sessionId);
timer.setSeatId(null);
timer.setStatus("RUNNING");
timer.setDurationSeconds(durationSeconds);
timer.setStartTime(now);
timer.setEndTime(end);
timer.setTenantId(tenantId);  // <-- 删除这行
timer.setDeleteFlag("0");
cashierServiceTimerMapper.insertSelective(timer);
```

**修改后：**
```java
CashierServiceTimer timer = new CashierServiceTimer();
timer.setSessionId(sessionId);
timer.setSeatId(null);
timer.setStatus("RUNNING");
timer.setDurationSeconds(durationSeconds);
timer.setStartTime(now);
timer.setEndTime(end);
timer.setDeleteFlag("0");
cashierServiceTimerMapper.insertSelective(timer);
```

**注意：**
- `updateByPrimaryKeySelective` 调用中的 `setTenantId` 可以保留，因为更新操作需要指定租户ID作为 WHERE 条件的一部分
- 只有插入操作需要移除，因为插入时 MyBatis-Plus 会自动注入 tenant_id

## 验证步骤

1. 重新编译后端代码
2. 重新部署应用
3. 在前端点击"开始服务计时"按钮
4. 确认不再出现 `tenant_id specified twice` 错误
5. 确认数据正确插入到数据库中，且 `tenant_id` 字段自动填充

## 备选方案（如果上述方案不适用）

如果业务逻辑要求在代码中显式设置 tenantId，则需要在 Mapper XML 中添加 `tenant_id` 的条件判断（与其他 Mapper 保持一致）：

在 `CashierServiceTimerMapper.xml` 的 `insertSelective` 中添加：
```xml
<if test="tenantId != null">
  tenant_id,
</if>
```

并在 values 部分添加：
```xml
<if test="tenantId != null">
  #{tenantId,jdbcType=BIGINT},
</if>
```

但这需要同时禁用 MyBatis-Plus 对此表的自动 tenant_id 注入，不推荐。

## 建议

推荐采用方案1（移除手动设置 tenantId），因为：
1. 符合项目现有架构设计（使用 MyBatis-Plus 多租户插件）
2. 与其他表的处理方式一致
3. 减少代码冗余
4. 降低出错风险
