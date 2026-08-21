# DotJob · 定时任务调度管理平台

> 基于 **.NET 10 + Quartz.NET + MySQL** 实现的 HTTP Job 调度管理平台，提供 Web UI 与 REST API，支持任务的增删改查、立即执行、日志追踪、操作审计、用户权限管理与钉钉推送通知。

[![Build & Publish Docker Image](https://github.com/liny005/WebJob/actions/workflows/docker-publish.yml/badge.svg)](https://github.com/liny005/WebJob/actions/workflows/docker-publish.yml)
[![Docker Hub](https://img.shields.io/badge/dockerhub-linhui2012%2Fdotjob-blue?logo=docker)](https://hub.docker.com/r/linhui2012/dotjob)

## 🚀 快速启动（无需拉取代码）

**在线体验：** http://43.136.91.134:8080/index.html　账号 `admin` / `admin123`

只需要有一个 MySQL 实例，一条命令即可运行：

```bash
docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;User ID=<user>;Password=<password>;Database=job;CharSet=utf8mb4;Minimum Pool Size=5;Maximum Pool Size=100;Connection Timeout=30;Default Command Timeout=30" \
  linhui2012/dotjob:latest
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
| 数据访问 | MySqlConnector（原生 ADO.NET，无 ORM） |
| 认证 | Cookie 认证（ASP.NET Core Authentication） |
| 前端 | HTML5 + Bootstrap 5 + 原生 JavaScript |
| 序列化 | System.Text.Json |
| 单元测试 | xUnit + FluentAssertions + Quartz RAMJobStore |

---

## 功能特性

- **任务管理**：新增 / 修改 / 删除 / 暂停 / 恢复 / 立即执行，支持 Cron 表达式和固定间隔两种触发方式，可设置执行次数上限和结束时间
- **HTTP Job**：支持 GET / POST / PUT / DELETE，自定义请求头与请求体，记录完整执行日志（耗时、状态码、响应内容）
- **用户管理**：Cookie 认证，Admin / User 双角色，密码 SHA256 + Salt 加密
- **操作审计**：记录登录、任务变更等关键操作，支持按操作人和功能类型筛选
- **推送通知**：钉钉机器人（Webhook + 签名）、邮件，可按成功 / 失败 / 全部独立配置触发策略
- **系统监控**：实时 CPU、内存、线程池、Job 执行延迟，Grafana 风格历史趋势图

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
│       ├── index.html                # 主页面（SPA 壳，加载各模块）
│       ├── login.html                # 登录页面
│       ├── js/app.js                 # 前端核心逻辑（路由 + 模块管理）
│       └── partials/                 # 页面模块（按需加载）
│           ├── jobs.html             # 任务管理页
│           ├── monitor.html          # 系统监控页
│           ├── audit.html            # 操作审计页
│           ├── notify.html           # 推送配置页
│           └── users.html            # 用户管理页
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
docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;User ID=<user>;Password=<password>;Database=job;CharSet=utf8mb4;Minimum Pool Size=5;Maximum Pool Size=100;Connection Timeout=30;Default Command Timeout=30" \
  linhui2012/dotjob:latest
```

### 方式二：从源码自行构建

```bash
git clone https://github.com/liny005/WebJob.git
cd WebJob

docker build -f DotJob_Scheduler/Dockerfile -t dotjob:latest .

docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=Server=<host>;Port=3306;User ID=<user>;Password=<password>;Database=job;CharSet=utf8mb4;Minimum Pool Size=5;Maximum Pool Size=100;Connection Timeout=30;Default Command Timeout=30" \
  dotjob:latest
```

### 前提条件

准备好一个 MySQL 8.x 实例，MySQL 用户需要有 `CREATE DATABASE`、`CREATE TABLE` 权限（首次启动时应用自动建库建表）。

### 可用镜像标签

| 标签 | 说明 |
|---|---|
| `latest` | main 分支最新构建 |

### Docker 相关文件

```
DotJob/
├── DotJob_Scheduler/
│   └── Dockerfile                      # 多阶段构建（SDK build → ASP.NET Runtime）
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
    "MysqlConnection": "Server=localhost;Port=3306;User ID=root;Password=your_password;Database=job;CharSet=utf8mb4;Minimum Pool Size=20;Maximum Pool Size=100"
  },
  "Quartz": {
    "DbProviderName": "MySqlConnector",
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
| `Minimum Pool Size` | 连接池最小连接数 | 20 |
| `Maximum Pool Size` | 连接池最大连接数 | 100 |
| `Connection Timeout` | 连接超时（秒） | 15 |
| `Default Command Timeout` | SQL 执行超时（秒） | 15 |

> **并发任务较多时，需同步调整连接池与线程池：**
> - `Maximum Pool Size` 建议 ≥ `MaxConcurrency`，否则高并发时任务会因等待连接而延迟
> - `MaxConcurrency` 与 `MaxBatchSize` 保持一致，避免触发批次不足导致的调度延迟
> - 示例：并发 200 个任务时，`Maximum Pool Size=200`、`Quartz__MaxConcurrency=200`、`Quartz__MaxBatchSize=200`

### Quartz 线程池

`MaxConcurrency` 和 `MaxBatchSize` 从配置读取，默认值均为 `100`，可通过 `appsettings.json` 或 Docker 环境变量覆盖，无需修改代码。

**Docker 环境变量方式：**

```bash
docker run -d \
  --name dotjob \
  -p 8080:8080 \
  -e "ConnectionStrings__MysqlConnection=..." \
  -e "Quartz__MaxConcurrency=200" \
  -e "Quartz__MaxBatchSize=200" \
  linhui2012/dotjob:latest
```

**appsettings.json 方式：**

```json
{
  "Quartz": {
    "MaxConcurrency": 200,
    "MaxBatchSize": 200
  }
}
```

> 根据实际任务并发量调整 `MaxConcurrency`，过小会导致任务排队延迟。

### MySQL 字符集

数据库和所有表必须使用 `utf8mb4_unicode_ci` 校对集，避免出现 `Illegal mix of collations` 错误。

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

A: MySQL 8 默认使用 `caching_sha2_password` 认证。MySqlConnector 原生支持该认证方式，无需额外配置。若仍失败，可将 MySQL 用户改为 `mysql_native_password`：
```sql
ALTER USER 'youruser'@'%' IDENTIFIED WITH mysql_native_password BY 'yourpassword';
FLUSH PRIVILEGES;
```

**Q: 任务数量多时出现排队延迟？**

A: 需要同步调整线程池和 MySQL 连接池，两者都是瓶颈：
1. `Quartz__MaxConcurrency` 和 `Quartz__MaxBatchSize` 建议 ≥ 并发任务数
2. `Maximum Pool Size` 建议 ≥ `MaxConcurrency`，连接池不足会让任务卡在等待连接上
3. Job 中是否有同步阻塞操作（HTTP 请求应使用异步）

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
