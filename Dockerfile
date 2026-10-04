# 飞书导出 · 命令行版容器镜像
#
# 构建：
#   docker build -t feishu-export:1.1.0 --build-arg VERSION=1.1.0 .
#
# 手动跑一次增量：
#   docker run --rm -e FEISHU_APP_ID=xxx -e FEISHU_APP_SECRET=xxx \
#     -e SPACE_ID=xxx -v "$PWD/exported:/data" feishu-export:1.1.0
#
# 常驻定时（推荐用 docker-compose.yml，见该文件注释）：
#   再加 -e AT=03:00 或 -e INTERVAL=6h，并配 --restart unless-stopped
#
# 镜像里只编译命令行版：容器没有显示设备，图形界面白白多几十 MB。
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

ARG VERSION=1.1.0
WORKDIR /src

COPY src/FeishuDocExport.Core/ src/FeishuDocExport.Core/
COPY src/FeishuDocExport.Cli/ src/FeishuDocExport.Cli/

RUN dotnet publish src/FeishuDocExport.Cli/FeishuDocExport.Cli.csproj \
      -c Release -r linux-x64 --self-contained false \
      -p:Version="$VERSION" -o /publish

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime

# fontconfig：Aspose.Words 做 docx → markdown 转换时要读字体，缺了会在导出 md 那一步失败
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates fontconfig \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /publish/ ./
COPY docker/entrypoint.sh /usr/local/bin/feishu-export-entrypoint
RUN chmod +x /usr/local/bin/feishu-export-entrypoint

ENV EXPORT_PATH=/data \
    DOC_TYPE=wiki \
    SAVE_TYPE=docx \
    INCREMENTAL=1

# 导出目录和增量状态文件都在这里，挂出来才能跨重启续跑
VOLUME ["/data"]

# CLI 里注册了 SIGTERM 处理：docker stop 会先把状态文件写完再退，不会留下半截进度
STOPSIGNAL SIGTERM

ENTRYPOINT ["/usr/local/bin/feishu-export-entrypoint"]
