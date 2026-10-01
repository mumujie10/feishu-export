#!/usr/bin/env bash
#
# 把图形界面打包成 macOS 的 .dmg 安装包。
#
# 用法：
#   ./scripts/build-dmg.sh                 # 默认打包当前机器架构（Apple Silicon → osx-arm64）
#   ./scripts/build-dmg.sh osx-x64         # 打包 Intel 版
#   ./scripts/build-dmg.sh osx-arm64 0.0.6 # 指定版本号
#
# 产物：dist/feishu-doc-export-gui-<version>-<rid>.dmg
#
# 说明：本脚本不依赖 Xcode，只用到 macOS 自带的 hdiutil / iconutil / codesign / plutil。
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

RID="${1:-osx-arm64}"
VERSION="${2:-0.0.5}"

APP_NAME="飞书文档导出工具"
BUNDLE_ID="com.feisudoc.export.gui"
EXECUTABLE="feishu-doc-export-gui"

case "$RID" in
  osx-arm64) ARCH_LABEL="Apple Silicon" ;;
  osx-x64)   ARCH_LABEL="Intel" ;;
  *) echo "不支持的 RID：$RID（本脚本只处理 osx-arm64 / osx-x64）" >&2; exit 1 ;;
esac

BUILD_DIR="$ROOT/build/dmg-$RID"
PAYLOAD_DIR="$BUILD_DIR/payload"
STAGE_DIR="$BUILD_DIR/stage"
APP_BUNDLE="$STAGE_DIR/$APP_NAME.app"
DMG_PATH="$ROOT/dist/feishu-doc-export-gui-$VERSION-$RID.dmg"
DOTNET="$ROOT/.tools/dn.sh"

if [[ ! -x "$DOTNET" ]]; then
  echo "找不到 $DOTNET，请先执行 .tools/dn.sh 确认 .NET SDK 已安装。" >&2
  exit 1
fi

echo "==> 清理旧的构建目录"
rm -rf "$BUILD_DIR" "$DMG_PATH"
mkdir -p "$PAYLOAD_DIR/gui" "$PAYLOAD_DIR/cli" "$APP_BUNDLE/Contents/MacOS" "$APP_BUNDLE/Contents/Resources"

echo "==> 编译并发布 GUI（$RID，自包含单文件）"
"$DOTNET" publish src/FeishuDocExport.Gui/FeishuDocExport.Gui.csproj \
  -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true \
  -p:Version="$VERSION" \
  -o "$PAYLOAD_DIR/gui" >/dev/null

echo "==> 编译并发布命令行版（$RID，自包含单文件）"
"$DOTNET" publish src/FeishuDocExport.Cli/FeishuDocExport.Cli.csproj \
  -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true \
  -p:Version="$VERSION" \
  -o "$PAYLOAD_DIR/cli" >/dev/null

echo "==> 生成应用图标"
python3 scripts/make-icon.py "$BUILD_DIR/icon" >/dev/null

echo "==> 组装 .app bundle"
cp "$PAYLOAD_DIR/gui/$EXECUTABLE" "$APP_BUNDLE/Contents/MacOS/$EXECUTABLE"
chmod +x "$APP_BUNDLE/Contents/MacOS/$EXECUTABLE"
cp "$BUILD_DIR/icon/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"

cat > "$APP_BUNDLE/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key>
    <string>zh_CN</string>
    <key>CFBundleDisplayName</key>
    <string>$APP_NAME</string>
    <key>CFBundleExecutable</key>
    <string>$EXECUTABLE</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>$BUNDLE_ID</string>
    <key>CFBundleInfoDictionaryVersion</key>
    <string>6.0</string>
    <key>CFBundleName</key>
    <string>$APP_NAME</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$VERSION</string>
    <key>CFBundleVersion</key>
    <string>$VERSION</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.productivity</string>
    <key>LSMinimumSystemVersion</key>
    <string>11.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Apache License 2.0</string>
</dict>
</plist>
PLIST

printf 'APPL????' > "$APP_BUNDLE/Contents/PkgInfo"

echo "==> 校验 Info.plist"
plutil -lint "$APP_BUNDLE/Contents/Info.plist" >/dev/null

echo "==> 临时签名（ad-hoc）"
# 没有 Apple 开发者证书时只能用 ad-hoc 签名：能避免「应用已损坏」，
# 但首次打开仍会被 Gatekeeper 拦下，需要右键打开或在「隐私与安全性」里放行。
codesign --force --deep --sign - "$APP_BUNDLE" 2>/dev/null
codesign --verify --deep --strict "$APP_BUNDLE" && echo "    签名校验通过"

echo "==> 准备 DMG 内容"
mkdir -p "$STAGE_DIR/命令行工具"
cp "$PAYLOAD_DIR/cli/feishu-doc-export" "$STAGE_DIR/命令行工具/feishu-doc-export"
chmod +x "$STAGE_DIR/命令行工具/feishu-doc-export"
ln -s /Applications "$STAGE_DIR/Applications"

cat > "$STAGE_DIR/使用说明.txt" <<'README'
飞书文档导出工具
================================================================

安装
----------------------------------------------------------------
把「飞书文档导出工具.app」拖到右侧的 Applications 文件夹即可。

首次打开被系统拦下怎么办
----------------------------------------------------------------
本程序没有购买 Apple 开发者证书，因此没有做公证（notarization）。
第一次打开时 macOS 会提示「无法验证开发者」，按下面任一方式放行即可：

方式一（推荐）：在「访达」里右键点击 App → 选择「打开」→ 在弹窗里再点一次「打开」。
方式二：打开「系统设置 → 隐私与安全性」，在底部点击「仍要打开」。
方式三：在终端执行一次
        xattr -dr com.apple.quarantine "/Applications/飞书文档导出工具.app"

只需放行一次，之后就能正常双击打开了。

使用前准备
----------------------------------------------------------------
1. 在飞书开放平台创建企业自建应用，开通云文档相关权限并发布；
2. 把应用作为群机器人加入一个群，再把这个群加为知识库管理员；
3. 在 App 里填入 AppId / AppSecret，点「获取列表」选择知识库，即可开始导出。

详细步骤见项目主页的 readme.md。

命令行版本
----------------------------------------------------------------
DMG 里的「命令行工具」文件夹中还有一个 feishu-doc-export，
适合挂机批量和写进脚本使用。用法：

    ./feishu-doc-export --help
README

echo "==> 生成 DMG"
hdiutil create \
  -volname "$APP_NAME" \
  -srcfolder "$STAGE_DIR" \
  -fs HFS+ \
  -format UDZO \
  -ov \
  "$DMG_PATH" >/dev/null

echo "==> 校验 DMG"
hdiutil verify "$DMG_PATH" >/dev/null && echo "    DMG 校验通过"

SIZE="$(du -h "$DMG_PATH" | cut -f1)"
echo
echo "打包完成：$DMG_PATH"
echo "  架构：$RID（$ARCH_LABEL）"
echo "  版本：$VERSION"
echo "  体积：$SIZE"
