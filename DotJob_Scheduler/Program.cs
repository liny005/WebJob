using DotJob_Core.DateTimeExtend;
using Job_Scheduler;
using Job_Scheduler.Application.Jobs;
using Job_Scheduler.Application.Notify;
using Job_Scheduler.Application.User;
using Job_Scheduler.Filters;
using Microsoft.AspNetCore.Authentication.Cookies;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// 初始化 AppConfig 配置
AppConfig.Initialize(builder.Configuration);


// Quartz 调度器 — AdoJobStore（MySQL 持久化）
builder.Services.AddQuartz(q =>
{
    q.SchedulerName = AppConfig.SchedulerName;
    q.SchedulerId = "AUTO";

    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 300);
    q.MaxBatchSize = 300;

    q.UsePersistentStore(store =>
    {
        store.UseProperties = false;
        store.RetryInterval = TimeSpan.FromSeconds(15);

        // 单机部署不需要集群
        // store.UseClustering();

        store.UseMySql(db =>
        {
            db.ConnectionString = AppConfig.ConnectionString;
            db.TablePrefix = "QRTZ_";
        });
        store.UseSystemTextJsonSerializer();
    });

    // 使用自定义 JobFactory 支持依赖注入
    q.UseJobFactory<JobFactory>();
});

// Quartz 后台服务（自动 Start 调度器）
builder.Services.AddQuartzHostedService(options => { options.WaitForJobsToComplete = true; });


// 添加 Cookie 认证
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login.html";
        options.LogoutPath = "/api/auth/logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(24);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

// 注册服务
builder.Services.AddSingleton<SchedulerCenterServices>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<DingTalkService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<NotifyService>();

builder.Services.AddControllers(m => { m.Filters.Add<ResultFilter>(); })
    .ConfigureApiBehaviorOptions(m => { m.SuppressModelStateInvalidFilter = true; })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new DatetimeJsonConverter());
        options.JsonSerializerOptions.Converters.Add(new NullableDatetimeJsonConverter());
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// 启动时自动检查并创建数据库表结构（幂等，可重复执行）
await DatabaseInitializer.InitializeAsync(
    AppConfig.ConnectionString,
    app.Services.GetRequiredService<ILogger<Program>>());

app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

