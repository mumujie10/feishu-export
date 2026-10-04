#!/bin/sh
# 把环境变量拼成命令行参数，再 exec 出真正的程序。
#
# 凭证只从环境变量读（FEISHU_APP_ID / FEISHU_APP_SECRET），
# 不写进命令行，免得留在 ps 输出和镜像历史层里。
set -eu

BIN=/app/feishu-doc-export

fail() {
  echo "$1" >&2
  exit 1
}

[ -n "${FEISHU_APP_ID:-}" ] || fail "缺少环境变量 FEISHU_APP_ID"
[ -n "${FEISHU_APP_SECRET:-}" ] || fail "缺少环境变量 FEISHU_APP_SECRET"

DOC_TYPE=${DOC_TYPE:-wiki}
EXPORT_PATH=${EXPORT_PATH:-/data}
SAVE_TYPE=${SAVE_TYPE:-docx}

if [ "$DOC_TYPE" = "cloudDoc" ]; then
  [ -n "${FOLDER_TOKEN:-}" ] || fail "DOC_TYPE=cloudDoc 时必须设置 FOLDER_TOKEN"
else
  [ -n "${SPACE_ID:-}" ] || fail "DOC_TYPE=wiki 时必须设置 SPACE_ID（知识库 Id）"
fi

# 用位置参数累积，避免路径里有空格时被拆坏
set -- --exportPath="$EXPORT_PATH" --type="$DOC_TYPE" --saveType="$SAVE_TYPE"

if [ -n "${SPACE_ID:-}" ]; then set -- "$@" --spaceId="$SPACE_ID"; fi
if [ -n "${FOLDER_TOKEN:-}" ]; then set -- "$@" --folderToken="$FOLDER_TOKEN"; fi
if [ -n "${API_ENDPOINT:-}" ]; then set -- "$@" --apiEndpoint="$API_ENDPOINT"; fi
if [ -n "${LICENSE_PATH:-}" ]; then set -- "$@" --licensePath="$LICENSE_PATH"; fi
if [ "${INCREMENTAL:-0}" = "1" ]; then set -- "$@" --incremental; fi
if [ -n "${INTERVAL:-}" ]; then set -- "$@" --interval="$INTERVAL"; fi
if [ -n "${AT:-}" ]; then set -- "$@" --at="$AT"; fi

echo "飞书导出 · 容器模式"
echo "  知识库/目录 : ${SPACE_ID:-$FOLDER_TOKEN}（类型 $DOC_TYPE）"
echo "  保存格式    : $SAVE_TYPE"
echo "  导出目录    : $EXPORT_PATH"
echo "  增量        : ${INCREMENTAL:-0}"

if [ -n "${AT:-}" ]; then
  echo "  定时        : 每天 $AT"
elif [ -n "${INTERVAL:-}" ]; then
  echo "  定时        : 每 $INTERVAL"
else
  echo "  定时        : 未设置，只跑一次就退出"
fi
echo

# EXTRA_ARGS 故意不加引号，方便一次传多个参数；路径里有空格请改用上面的专用变量
# shellcheck disable=SC2086
exec "$BIN" "$@" ${EXTRA_ARGS:-}
