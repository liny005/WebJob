# ---------- 构建阶段 ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 先只拷贝 csproj 以利用 Docker 层缓存加速依赖还原
COPY DotJob_Core/DotJob_Core.csproj DotJob_Core/
COPY DotJob_Model/DotJob_Model.csproj DotJob_Model/
COPY DotJob_Scheduler/DotJob_Scheduler.csproj DotJob_Scheduler/
RUN dotnet restore DotJob_Scheduler/DotJob_Scheduler.csproj

# 拷贝其余源码并发布
COPY DotJob_Core/ DotJob_Core/
COPY DotJob_Model/ DotJob_Model/
COPY DotJob_Scheduler/ DotJob_Scheduler/
COPY scripts/ scripts/
RUN dotnet publish DotJob_Scheduler/DotJob_Scheduler.csproj -c Release -o /app/publish --no-restore

# ---------- 运行阶段 ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /src/scripts/init_database.sql .

# 容器内监听 8080 端口（.NET 8+ 官方镜像默认端口）
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "DotJob_Scheduler.dll"]
