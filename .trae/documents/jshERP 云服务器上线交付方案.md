## 结论（你截图这台能不能用）
- 你截图的腾讯云轻量：2核2G、OpenCloudOS8、已带Docker，作为“客户验收测试/试用环境”可以用。
- 若客户要直接跑正式生产：2G内存偏紧（Java+MySQL+Redis+Nginx同机容易顶满），建议至少4G起，并优先把MySQL/Redis迁到云数据库/云缓存。

## 目标架构（推荐：Docker一键部署）
- Nginx：对外80/443，托管前端静态 + 反向代理后端
- 后端：Spring Boot（容器内运行，映射9999到内网）
- MySQL：容器（测试可同机；生产建议云MySQL）
- Redis：容器（测试可同机；生产建议云Redis）
- 文件上传：落本机磁盘挂载卷（生产可升级到OSS）

## 交付清单（你交给客户的材料）
- 前端构建产物：jshERP-web/dist（静态文件）
- 后端构建产物：jshERP-boot打包出的jshERP.jar（或dist发布包）
- 数据库初始化：jshERP-boot/docs/jsh_erp.sql（首次安装）
- 增量升级脚本：docs/upgrade_*.sql（如有则按顺序执行）
- 配置项清单：数据库、Redis、文件路径、注册/验证码开关、域名、HTTPS证书

## 上线步骤（可直接照抄执行）
### 1）云侧准备
- 安全组/防火墙：开放80、443；SSH 22限制到你的管理IP；关闭对外的9999、3306、6379。
- 域名解析：A记录指向轻量服务器公网IP。

### 2）服务器目录规划（统一放到/opt，便于备份与回滚）
- /opt/jshERP/
  - web/（前端dist）
  - backend/（后端jar与外置配置）
  - mysql/（MySQL数据卷）
  - redis/（Redis数据卷）
  - upload/（业务上传文件）
  - logs/（Nginx/后端日志）

### 3）数据库与缓存（Docker）
- 启动MySQL容器：挂载数据卷；设置root强密码；创建数据库jsh_erp（utf8mb4）。
- 导入初始化SQL：执行jshERP-boot/docs/jsh_erp.sql。
- 启动Redis容器：设置requirepass（生产必须）。

### 4）后端容器（Spring Boot）
- 外置配置：用发布包的config/application.properties作为模板，关键项：
  - spring.datasource.url/username/password（指向MySQL容器或云MySQL）
  - spring.redis.host/port/password（指向Redis容器或云Redis）
  - server.port=9999、server.servlet.context-path=/jshERP-boot
  - file.path=/opt/jshERP/upload、server.tomcat.basedir=/opt/jshERP/tomcat
  - manage.roleId=10（注册租户角色），tenant.tryDayLimit/tenant.userNumLimit按合同调整
- 启动后端：容器运行jshERP.jar，挂载config与upload目录，日志输出到/opt/jshERP/logs。

### 5）前端静态发布 + 反代
- 将jshERP-web/dist全部上传到/opt/jshERP/web/。
- Nginx站点：
  - / -> /opt/jshERP/web（前端）
  - /jshERP-boot/ -> 代理到后端容器（http://127.0.0.1:9999/jshERP-boot/）
- 说明：前端默认API根是“/jshERP-boot”，并且首屏会同步请求平台参数，所以该路径必须可达。

### 6）平台参数与注册策略（上线前必配）
- 平台参数接口：/platformConfig/getPlatform/registerFlag、checkcodeFlag
- 建议：
  - 测试/试用环境：register_flag=1（允许注册租户），checkcode_flag=1（开启验证码）
  - 正式生产：register_flag=0（关闭公开注册，改为你们后台开通），checkcode_flag=1
- 账号角色口径：注册的是“租户管理员”，自动创建jsh_user+jsh_tenant，并赋予“租户”角色（默认roleId=10）。

## 验收清单（交付给客户的验收点）
- 访问：域名可打开；HTTPS正常；静态资源加载无404
- 登录：管理员/租户登录正常；token正常；退出正常
- 注册（若开启）：可注册租户、注册后可登录、注册后默认角色为“租户”
- 收银门店隔离：子账号门店列表只显示授权门店；跨门店请求被拒绝
- 上传：图片/附件上传后可访问；磁盘占用可控
- 日志：Nginx/后端日志可查；出现异常能定位

## 备份、回滚与运维（生产建议写进合同交付）
- 备份：MySQL每日全量+保留周期；上传文件目录定期备份；重要升级前做一次手工快照
- 回滚：保留上一版后端镜像/目录与前端dist；升级失败可一键切回
- 监控：CPU/内存/磁盘、容器存活、接口健康检查、MySQL慢查询

## 你确认后我将做的“落地补齐”（不改代码，只补交付细节）
- 把上面方案整理成“客户版交付文档结构”（含：端口表、目录表、验收表、回滚表）。
- 按你们实际域名与是否使用云MySQL/云Redis，给出对应的Nginx反代与容器参数模板。