# DotJob · 定时任务调度管理平台

> 基于 **.NET 10 + Quartz.NET + MySQL** 实现的 HTTP Job 调度管理平台，提供 Web UI 与 REST API，支持任务的增删改查、立即执行、日志追踪、操作审计、用户权限管理与钉钉推送通知。

[![Build & Publish Docker Image](https://github.com/liny005/WebJob/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/liny005/WebJob/actions/workflows/docker-publish.yml)
[![ghcr.io](https://img.shields.io/badge/ghcr.io-liny005%2Fwebjob-blue?logo=docker)](https://github.com/liny005/WebJob/pkgs/container/webjob)

## 🚀 快速启动（无需拉取代码）

只需要有一个 MySQL 实例，一条命令即可运行：

```bash
docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;Uid=<user>;Pwd=<password>;Database=job;Charset=utf8mb4;Min Pool Size=5;Max Pool Size=100;Connection Timeout=30;Default Command Timeout=30" \
  ghcr.io/liny005/webjob:latest
```

- 替换 `<host>`、`<user>`、`<password>` 为你的 MySQL 信息
- **首次启动**自动创建数据库和所有表，无需手动执行 SQL
- 浏览器访问 [http://localhost:8080](http://localhost:8080)，默认账号 `admin` / `admin123`

> **镜像支持平台：** `linux/amd64`、`linux/arm64`（Apple Silicon / 云服务器均可）

---

## 目录

- [项目简介](#项目简介)
- [技术栈](#技术栈)
- [功能特性](#功能特性)
- [项目结构](#项目结构)
- [Docker 部署](#docker-部署)
- [快速上手](#快速上手)
- [配置说明](#配置说明)
- [API 文档](#api-文档)
- [数据库表结构](#数据库表结构)
- [单元测试](#单元测试)
- [设计说明](#设计说明)
- [常见问题](#常见问题)

---

## 项目简介

DotJob 是一个轻量级的定时任务调度管理平台。核心能力是通过 HTTP 请求周期性地调用第三方接口（HTTP Job），并提供完整的任务生命周期管理、执行日志、操作审计以及钉钉通知推送。

**适用场景：**
- 定时调用业务接口（数据同步、报表生成、缓存刷新等）
- 替代 Cron 脚本，统一管理所有定时任务
- 需要追踪任务执行记录与操作审计的场景

---

## 技术栈

| 层次 | 技术 |
|------|------|
| 运行时 | .NET 10 (ASP.NET Core) |
| 定时调度 | Quartz.NET 3.x（AdoJobStore 持久化） |
| 数据库 | MySQL 8.x |
| 数据访问 | MySql.Data（原生 ADO.NET，无 ORM） |
| 认证 | Cookie 认证（ASP.NET Core Authentication） |
| 前端 | HTML5 + Bootstrap 5 + 原生 JavaScript |
| 序列化 | System.Text.Json |
| 单元测试 | xUnit + FluentAssertions + Quartz RAMJobStore |

---

## 功能特性

### 任务管理
- ✅ 新增任务（支持 **Cron 表达式** 和 **固定间隔秒数** 两种触发方式）
- ✅ 修改任务（任务名称与分组不可变，其他配置均可修改）
- ✅ 删除任务（同步清理执行日志与配置）
- ✅ 暂停 / 恢复任务
- ✅ **立即执行**（手动触发一次，不影响原有调度计划）
- ✅ 任务列表查询（支持按名称、分组模糊筛选）
- ✅ 任务详情查看
- ✅ 执行次数上限（到达上限后自动暂停）
- ✅ 开始 / 结束时间控制（结束时间到达后自动暂停）
- ✅ 并发执行（Quartz 线程池最大 200 并发，支持大量任务同时触发）

### HTTP Job 执行
- ✅ 支持 GET / POST / PUT / DELETE 请求
- ✅ 自定义请求头（如 `Authorization`）
- ✅ 自定义请求体（JSON 格式）
- ✅ 执行日志：开始时间、结束时间、耗时、HTTP 状态码、响应内容、错误信息

### 用户管理（Admin 专属）
- ✅ 用户列表（分页，每页 20 条）
- ✅ 新增用户 / 删除用户
- ✅ Admin 不可删除自己
- ✅ 基于 Cookie 的登录 / 登出
- ✅ 修改密码（SHA256 + Salt 加密）
- ✅ 角色权限（Admin / User）

### 操作审计日志
- ✅ 记录所有关键操作：登录、新增任务、修改任务、删除任务、手动触发等
- ✅ 记录字段：操作人、操作功能、操作对象、备注（含请求参数 JSON）、操作时间
- ✅ 修改操作的请求参数可点击查看详情（JSON 格式弹窗展示）
- ✅ 操作功能下拉筛选

### 推送通知（Admin 专属）
- ✅ 钉钉机器人推送（WebhookUrl + Secret 签名验证）
- ✅ 配置卡片式 UI
- ✅ 可扩展支持飞书 / 邮件（预留入口）
- ✅ 推送配置变更记录审计日志

### 前端界面
- ✅ 响应式 Bootstrap 5 UI
- ✅ 任务列表带统计卡片（总数 / 开启 / 暂停）
- ✅ 任务操作按钮 Tooltip 提示
- ✅ 新增 / 修改表单加载动画，提交期间禁用交互
- ✅ 自动刷新（可配置刷新间隔，最小 5 秒）
- ✅ 执行日志弹窗（分页查看）、日志详情弹窗（嵌套，按 ESC 仅关闭最顶层）
- ✅ 时间统一格式：`2026-02-25 16:05:39`

---

## 项目结构

```
DotJob/
├── DotJob_Core/                    # 公共工具类库
│   ├── HttpHelper.cs               # 封装 HttpClient 请求
│   └── DateTimeExtend/             # 日期时间扩展与 JSON 转换
│
├── DotJob_Model/                   # 业务模型层（实体 / DTO / 枚举）
│   ├── Entity/
│   │   ├── JobConfig.cs            # 任务扩展配置（对应 JOB_CONFIG 表）
│   │   ├── JobListInfo.cs          # 任务列表 DTO
│   │   ├── JobDetailInfo.cs        # 任务详情 DTO
│   │   ├── LogEntity.cs            # 执行日志实体
│   │   ├── AuditLog.cs             # 操作审计日志实体
│   │   ├── NotifyConfig.cs         # 推送配置实体
│   │   └── UserEntity.cs           # 用户实体
│   ├── Auth/                       # 登录请求 / 响应 DTO
│   ├── Enums/                      # 枚举定义（触发器类型、请求类型等）
│   └── WebJobs/
│       └── AddWebJobs.cs           # 新增 / 修改任务请求 DTO
│
├── DotJob_Scheduler/               # 主应用程序
│   ├── Program.cs                  # 程序入口，DI 注册，Quartz 配置
│   ├── AppConfig.cs                # 全局配置读取（连接串、调度器名称）
│   ├── appsettings.json            # 应用配置文件
│   ├── Application/
│   │   ├── Jobs/
│   │   │   ├── HttpJob.cs          # HTTP Job 实现（继承 JobBase）
│   │   │   ├── JobBase.cs          # Job 基类（结束时间/次数控制、日志写入）
│   │   │   ├── JobFactory.cs       # Quartz Job 工厂（支持 DI）
│   │   │   └── SchedulerCenterServices.cs  # 调度服务（核心业务逻辑）
│   │   ├── Notify/
│   │   │   └── NotifyService.cs    # 推送通知服务
│   │   └── User/
│   │       └── AuthService.cs      # 用户认证服务
│   ├── Controllers/
│   │   ├── JobScheduleController.cs  # 任务管理 API
│   │   ├── AuthController.cs         # 认证 API
│   │   ├── UserController.cs         # 用户管理 API
│   │   ├── AuditLogController.cs     # 审计日志 API
│   │   └── NotifyController.cs       # 推送配置 API
│   ├── Filters/
│   │   └── ResultFilter.cs           # 统一响应包装过滤器
│   └── wwwroot/                      # 前端静态文件
│       ├── index.html                # 主页面（任务列表）
│       ├── login.html                # 登录页面
│       └── js/app.js                 # 前端核心逻辑
│
├── DotJob_Tests/                   # 单元测试项目
│   ├── SchedulerCenterServicesTests.cs   # 服务层测试（26 个测试用例）
│   └── Infrastructure/
│       ├── TestableSchedulerCenterServices.cs  # 可测试子类（内存调度器 + 内存DB）
│       └── JobInputBuilder.cs              # 测试数据构建器
│
└── scripts/
    └── init_database.sql           # 数据库初始化脚本（含 Quartz 表 + 业务表）
```

---

## Docker 部署

> 应用启动时会**自动检查并创建**所有数据库表结构（幂等，可重复执行）。

### 方式一：直接使用发布的镜像（推荐）

无需拉取代码，直接 pull 运行：

```bash
# 最新版
docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;Uid=<user>;Pwd=<password>;Database=job;Charset=utf8mb4;Min Pool Size=5;Max Pool Size=100;Connection Timeout=30;Default Command Timeout=30" \
  ghcr.io/liny005/webjob:latest

# 指定版本（推荐生产环境固定版本号）
docker run -d ... ghcr.io/liny005/webjob:1.0.0
```

### 方式二：从源码自行构建

```bash
git clone https://github.com/liny005/WebJob.git
cd WebJob

docker build -t dotjob:latest .

docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;..." \
  dotjob:latest
```

### 前提条件

准备好一个 MySQL 8.x 实例，MySQL 用户需要有 `CREATE DATABASE`、`CREATE TABLE` 权限（首次启动时应用自动建库建表）。

### 可用镜像标签

| 标签 | 说明 |
|---|---|
| `latest` | main 分支最新构建 |
| `1.2.3` | 对应 Git tag `v1.2.3` 的稳定版本 |
| `1.2` | 该次要版本的最新构建 |

### Docker 相关文件

```
DotJob/
├── Dockerfile                          # 多阶段构建（SDK build → ASP.NET Runtime）
├── .dockerignore                       # 排除 bin/obj/.git 等无关文件
├── .github/workflows/docker-publish.yml  # CI：push main/tag 自动构建并推送镜像
└── scripts/
    └── init_database.sql               # 随应用发布，启动时自动执行
```

---

## 快速上手

### 1. 环境准备

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MySQL 8.x

### 2. 创建数据库

```sql
CREATE DATABASE job
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;
```

### 3. 初始化表结构

```bash
mysql -u root -p job < scripts/init_database.sql
```

脚本会自动创建以下内容：
- 业务表：`JOB_USER`、`JOB_LOG`、`JOB_CONFIG`、`JOB_AUDIT_LOG`、`JOB_NOTIFY_CONFIG_JSON`
- Quartz 持久化表：`QRTZ_JOB_DETAILS`、`QRTZ_TRIGGERS` 等 11 张表
- 默认管理员账户：`admin` / `admin123`

### 4. 修改连接字符串

编辑 `DotJob_Scheduler/appsettings.json`：

```json
{
  "ConnectionStrings": {
    "MysqlConnection": "Server=localhost;Port=3306;Uid=root;Pwd=your_password;Database=job;Charset=utf8mb4;Min Pool Size=20;Max Pool Size=100"
  },
  "Quartz": {
    "DbProviderName": "MySql",
    "SchedulerName": "jobScheduler"
  }
}
```

### 5. 运行

```bash
cd DotJob
dotnet run --project DotJob_Scheduler
```

### 6. 访问

打开浏览器访问 `http://localhost:5000`

| 账号 | 密码 | 角色 |
|------|------|------|
| admin | admin123 | Admin（拥有所有权限） |

> ⚠️ **生产环境请立即修改默认密码！**

---

## 配置说明

### 连接字符串参数

| 参数 | 说明 | 推荐值 |
|------|------|--------|
| `Min Pool Size` | 连接池最小连接数 | 20 |
| `Max Pool Size` | 连接池最大连接数 | 100 |
| `Connection Timeout` | 连接超时（秒） | 15 |
| `Default Command Timeout` | SQL 执行超时（秒） | 15 |

### Quartz 线程池

在 `Program.cs` 中配置：

```csharp
q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 200); // 最大并发 200
q.MaxBatchSize = 200;                                   // 每批最多获取 200 个待触发任务
```

> 根据实际任务并发量调整 `MaxConcurrency`，过小会导致任务排队延迟。

### MySQL 字符集

数据库和所有表必须使用 `utf8mb4_unicode_ci` 校对集，避免出现 `Illegal mix of collations` 错误。

---

## API 文档

所有接口统一通过 `ResultFilter` 包装为以下格式：

```json
{
  "success": true,
  "code": 0,
  "message": "成功",
  "timestamp": 1740000000000,
  "data": { }
}
```

### 认证接口

| 方法 | 路径 | 说明 | 需要登录 |
|------|------|------|----------|
| POST | `/api/auth/login` | 用户登录 | ❌ |
| POST | `/api/auth/logout` | 用户登出 | ✅ |
| GET | `/api/auth/current` | 获取当前用户信息 | ✅ |
| POST | `/api/auth/change-password` | 修改密码 | ✅ |

**登录请求示例：**
```json
{ "username": "admin", "password": "admin123" }
```

### 任务管理接口

| 方法 | 路径 | 说明 | 需要 Admin |
|------|------|------|-----------|
| GET | `/api/job/all` | 查询任务列表（支持名称/分组筛选） | ❌ |
| POST | `/api/job/add` | 新增任务 | ❌ |
| POST | `/api/job/update` | 修改任务 | ❌ |
| POST | `/api/job/pause` | 暂停任务 | ❌ |
| POST | `/api/job/resume` | 恢复任务 | ❌ |
| POST | `/api/job/triggerNow` | 立即执行一次 | ❌ |
| DELETE | `/api/job/delete` | 删除任务 | ❌ |
| GET | `/api/job/detail` | 获取任务详情 | ❌ |
| GET | `/api/job/logs` | 查询任务执行日志（分页） | ❌ |

**新增任务请求示例：**
```json
{
  "jobName": "同步用户数据",
  "jobGroup": "数据同步",
  "triggerType": 2,
  "intervalSecond": 300,
  "requestUrl": "https://api.example.com/sync/users",
  "requestType": 2,
  "headers": "{\"Authorization\": \"Bearer your_token\"}",
  "requestParameters": "{\"type\": \"full\"}",
  "description": "每5分钟同步一次用户数据",
  "beginTime": "2026-01-01T00:00:00",
  "dingtalk": 1
}
```

**触发器类型（triggerType）：**

| 值 | 说明 |
|----|------|
| 1 | Cron 表达式（需填写 `cron` 字段） |
| 2 | 固定间隔（需填写 `intervalSecond` 字段，单位秒） |

**请求类型（requestType）：**

| 值 | 说明 |
|----|------|
| 1 | GET |
| 2 | POST |
| 3 | PUT |
| 4 | DELETE |

### 用户管理接口（Admin 专属）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/user/list` | 用户列表（分页，每页20条） |
| POST | `/api/user/add` | 新增用户 |
| DELETE | `/api/user/delete` | 删除用户（不可删除自己） |

### 审计日志接口

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/auditlog/list` | 审计日志列表（分页，支持操作类型筛选） |

### 推送配置接口（Admin 专属）

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/notify/list` | 获取推送配置列表 |
| POST | `/api/notify/save` | 保存推送配置（新增或更新） |
| DELETE | `/api/notify/delete/{id}` | 删除推送配置 |

---

## 数据库表结构

### JOB_USER（用户表）

| 字段 | 类型 | 说明 |
|------|------|------|
| ID | BIGINT | 主键，自增 |
| USERNAME | VARCHAR(50) | 用户名（唯一） |
| PASSWORD | VARCHAR(256) | 密码（SHA256 + Salt 加密） |
| DISPLAY_NAME | VARCHAR(100) | 显示名称 |
| EMAIL | VARCHAR(100) | 邮箱 |
| IS_ENABLED | TINYINT(1) | 是否启用 |
| ROLE | VARCHAR(20) | 角色：`Admin` / `User` |
| CREATED_AT | DATETIME | 创建时间 |
| LAST_LOGIN_AT | DATETIME | 最后登录时间 |

### JOB_CONFIG（任务扩展配置表）

替代 Quartz JobDataMap，将业务配置从调度引擎中剥离，减少 Quartz 表 IO。

| 字段 | 类型 | 说明 |
|------|------|------|
| ID | BIGINT | 主键，自增 |
| JOB_NAME | VARCHAR(200) | 任务名称（与 Quartz JobKey.Name 对应） |
| JOB_GROUP | VARCHAR(200) | 任务分组（与 Quartz JobKey.Group 对应） |
| REQUEST_URL | VARCHAR(1000) | 请求 URL |
| REQUEST_TYPE | INT | 请求类型（1=GET 2=POST 3=PUT 4=DELETE） |
| HEADERS | TEXT | 请求头 JSON |
| REQUEST_PARAMETERS | TEXT | 请求参数 JSON |
| TRIGGER_TYPE | INT | 触发器类型（1=Cron 2=间隔秒） |
| CRON | VARCHAR(100) | Cron 表达式 |
| INTERVAL_SECOND | INT | 间隔秒数 |
| BEGIN_TIME | DATETIME | 任务开始时间 |
| END_TIME | DATETIME | 任务结束时间（null=永不结束） |
| RUN_TOTAL | INT | 执行次数上限（null=无限制） |
| DINGTALK | INT | 钉钉通知级别（0=不通知 1=仅失败 2=全部） |

### JOB_LOG（执行日志表）

| 字段 | 类型 | 说明 |
|------|------|------|
| ID | BIGINT | 主键，自增 |
| JOB_NAME | VARCHAR(200) | 格式：`分组.任务名` |
| BEGIN_TIME | DATETIME | 实际开始执行时间 |
| END_TIME | DATETIME | 执行结束时间 |
| EXECUTE_TIME | DOUBLE | 执行耗时（秒） |
| EXECUTION_STATUS | INT | 0=进行中 1=成功 2=失败 |
| URL | VARCHAR(500) | 请求 URL |
| REQUEST_TYPE | VARCHAR(20) | 请求类型 |
| PARAMETERS | TEXT | 请求参数 |
| RESULT | TEXT | 响应内容 |
| STATUS_CODE | INT | HTTP 状态码 |
| ERROR_MSG | TEXT | 错误信息 |

### JOB_AUDIT_LOG（操作审计日志表）

| 字段 | 类型 | 说明 |
|------|------|------|
| ID | BIGINT | 主键，自增 |
| OPERATOR | VARCHAR(50) | 操作人用户名 |
| OPERATOR_DISPLAY_NAME | VARCHAR(100) | 操作人显示名称 |
| ACTION | VARCHAR(100) | 操作功能（如：新增任务、删除任务） |
| TARGET | VARCHAR(200) | 操作对象（如：分组.任务名） |
| REMARK | TEXT | 备注（修改操作会记录请求参数 JSON） |
| CREATED_AT | DATETIME | 操作时间 |

### JOB_NOTIFY_CONFIG_JSON（推送配置表）

| 字段 | 类型 | 说明 |
|------|------|------|
| ID | BIGINT | 主键，自增 |
| NAME | VARCHAR(100) | 配置名称 |
| CHANNEL | VARCHAR(50) | 渠道类型：`DingTalk` / `Feishu` / `Email` |
| CONFIG | TEXT | 渠道配置 JSON（钉钉：`{"webhookUrl":"...","secret":"..."}`) |
| IS_ENABLED | TINYINT(1) | 是否启用 |

---

## 单元测试

### 运行测试

```bash
cd DotJob
dotnet test DotJob_Tests/DotJob_Tests.csproj
```

### 测试策略

测试项目使用 `TestableSchedulerCenterServices`（继承自 `SchedulerCenterServices`）：

- **内存调度器**：覆盖 `GetSchedulerAsync()`，注入纯内存 `RAMJobStore`，无需真实 Quartz DB
- **内存 DB 模拟**：覆盖所有 MySQL 方法（`UpsertJobConfigAsync`、`LoadJobConfigAsync`、`CountLogsAsync`、`GetJobEndTimeAsync`、`DeleteJobDataAsync`、`QueryJobAsync`），改用字典存储
- **完全隔离**：每个测试方法独享独立的调度器与内存存储实例，互不干扰
- **真实业务逻辑**：触发器构建、状态判断、异常校验等核心逻辑均执行真实代码

### 测试用例覆盖（26 个）

| 分类 | 测试内容 |
|------|----------|
| **AddJob** | 任务注册成功、RequestUrl/RunTotal 正确存储、Simple/Cron 触发器参数、重复添加抛异常、添加后可查询到 |
| **UpdateJob** | 间隔时间修改生效、RequestUrl 更新、RunNumber 不被重置、RunTotal 继承旧值、修改前暂停后修改仍暂停、不存在任务抛异常 |
| **TriggerJobNow** | 任务存在时不抛异常、任务不存在时抛异常含任务名 |
| **PauseJob** | 暂停后触发器状态为 Paused、列表中 TriggerState 为 2 |
| **ResumeJob** | 恢复后触发器状态为 Normal、结束时间过期时抛异常 |
| **DelJob** | 删除后调度器中不存在、查询列表不含该任务、GetJobDetail 返回 null |
| **GetJobDetail** | 字段与输入一致、不存在返回 null、RunNumber 初始为 0、SetLogCount 后反映正确值、Cron 类型字段正确 |
| **QueryJob** | 返回全部、无筛选全量返回、按名称模糊筛选、按分组筛选、空调度器返回空列表 |

---

## 设计说明

### Quartz 与业务数据分离

Quartz 只负责触发调度（触发时间、触发器状态），所有业务扩展数据（URL、请求参数、次数限制等）保存在独立的 `JOB_CONFIG` 表，不写入 `JobDataMap`。

**好处：**
- 减少 Quartz 表序列化/反序列化开销
- 业务字段变更不影响调度引擎
- 便于查询和维护

### 执行次数与结束时间控制

在 `JobBase.Execute` 中：
1. 检查 `manual_trigger` 标记：手动触发时跳过次数和结束时间限制
2. 检查结束时间：到期自动暂停（不删除）
3. 检查执行次数：达到上限自动暂停（不删除）

### 并发执行

Quartz 线程池配置 `MaxConcurrency = 200`，所有任务真正并行执行，互不阻塞。

---

## 常见问题

**Q: MySQL 连接失败 `caching_sha2_password`？**

A: MySQL 8 默认使用 `caching_sha2_password` 认证。确保 `MySql.Data` 驱动版本支持，或将用户认证方式改为 `mysql_native_password`：
```sql
ALTER USER 'youruser'@'%' IDENTIFIED WITH mysql_native_password BY 'yourpassword';
FLUSH PRIVILEGES;
```

**Q: 任务数量多时出现排队延迟？**

A: 检查以下配置：
1. `MaxConcurrency` 是否足够大（建议 ≥ 任务数量）
2. `MaxBatchSize` 应与 `MaxConcurrency` 保持一致
3. Job 中是否有同步阻塞操作（HTTP 请求应使用异步）
4. MySQL 连接池是否充足（`Max Pool Size` ≥ 并发任务数）

**Q: 出现 `Illegal mix of collations` 错误？**

A: 检查数据库、所有表的字符集和校对集，统一使用：
```sql
utf8mb4 + utf8mb4_unicode_ci
```

**Q: 上次执行时间大于下次执行时间？**

A: 列表的 `PreviousFireTime` / `NextFireTime` 直接从 Quartz 的 `QRTZ_TRIGGERS` 表读取（BIGINT ticks 转 DateTime），确保时区转换正确（UTC → 本地时间）。

**Q: 任务到结束时间后被自动删除？**

A: 正常情况是**暂停**不是删除。请检查 `JobBase` 中是否误调用了 `DeleteJob`，应只调用 `PauseJob`。

---

## License

MIT License
