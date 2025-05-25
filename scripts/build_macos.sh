#!/bin/bash

# 设置错误时退出
set -e

# 颜色定义
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# 打印带颜色的信息
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# 检查必要的工具
check_requirements() {
    print_info "检查必要的工具..."
    
    if ! command -v dotnet &> /dev/null; then
        print_error "未找到 dotnet，请安装 .NET SDK"
        print_info "可以从 https://dotnet.microsoft.com/download 下载"
        exit 1
    fi
    
    if ! command -v create-dmg &> /dev/null; then
        print_warning "未找到 create-dmg，将跳过 DMG 创建步骤"
        print_info "可以通过 brew install create-dmg 安装"
    fi
}

# 清理旧的构建文件
clean_build() {
    print_info "清理旧的构建文件..."
    rm -rf build/
    rm -rf dist/
}

# 创建说明文件
create_readme() {
    local target_dir=$1
    local readme_path="${target_dir}/README.txt"
    
    cat > "${readme_path}" << 'EOF'
首次运行说明：

1. 首次运行时，系统可能会显示"无法打开"的提示
2. 请点击"系统偏好设置"或"系统设置"
3. 在"安全性与隐私"中点击"仍要打开"
4. 之后就可以正常使用了

注意：这是正常的安全提示，只需要操作一次。
EOF
}

# 创建 Info.plist
create_info_plist() {
    local app_path=$1
    local info_plist="${app_path}/Contents/Info.plist"
    
    cat > "${info_plist}" << 'EOF'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>
    <string>TuneLab</string>
    <key>CFBundleIconFile</key>
    <string>AppIcon</string>
    <key>CFBundleIdentifier</key>
    <string>app.tunelab.editor</string>
    <key>CFBundleName</key>
    <string>TuneLab</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>1.0</string>
    <key>LSMinimumSystemVersion</key>
    <string>10.15</string>
    <key>NSHighResolutionCapable</key>
    <true/>
</dict>
</plist>
EOF
}

# 创建 .app 包
create_app_bundle() {
    local build_dir=$1
    local app_path="${build_dir}/TuneLab.app"
    
    print_info "创建 .app 包: ${app_path}"
    
    # 创建 .app 目录结构
    mkdir -p "${app_path}/Contents/MacOS"
    mkdir -p "${app_path}/Contents/Resources"
    
    # 移动可执行文件到 MacOS 目录
    mv "${build_dir}/TuneLab" "${app_path}/Contents/MacOS/"
    
    # 创建 Resources/Translations 目录
    mkdir -p "${app_path}/Contents/MacOS/Resources/Translations"
    
    # 复制 .toml 文件到 Resources/Translations 目录
    find "${build_dir}" -name "*.toml" -exec cp {} "${app_path}/Contents/MacOS/Resources/Translations/" \;
    
    # 复制其他运行时文件到 MacOS 目录（排除已移动的可执行文件和 .toml 文件）
    find "${build_dir}" -type f -not -name "TuneLab" -not -name "*.toml" -exec cp {} "${app_path}/Contents/MacOS/" \;
    
    # 创建 Info.plist
    create_info_plist "${app_path}"
    
    # 复制并重命名图标文件
    if [ -f "scripts/logo.icns" ]; then
        cp "scripts/logo.icns" "${app_path}/Contents/Resources/AppIcon.icns"
    fi
}

# 本地签名应用
sign_app_locally() {
    local app_path=$1
    print_info "本地签名应用: ${app_path}"
    
    # 移除可能存在的旧签名
    codesign --remove-signature "${app_path}" 2>/dev/null || true
    
    # 使用本地证书签名
    codesign --force --deep --sign - "${app_path}"
    
    # 验证签名
    codesign --verify --verbose "${app_path}"
    
    # 创建说明文件
    create_readme "$(dirname "${app_path}")"
}

# 构建应用
build_app() {
    local arch=$1
    local build_dir="build/${arch}"
    
    print_info "开始构建 ${arch} 版本..."
    
    # 创建构建目录
    mkdir -p "${build_dir}"
    
    # 使用 dotnet 构建应用
    dotnet publish TuneLab/TuneLab.csproj \
        -c Release \
        -r osx-${arch} \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "${build_dir}"
    
    # 创建 .app 包
    create_app_bundle "${build_dir}"
    
    # 本地签名应用
    sign_app_locally "${build_dir}/TuneLab.app"
}

# 创建 DMG
create_dmg() {
    local arch=$1
    local build_dir="build/${arch}"
    
    if command -v create-dmg &> /dev/null; then
        print_info "创建 ${arch} 版本的 DMG 文件..."
        
        create-dmg \
            --volname "TuneLab-${arch}" \
            --volicon "scripts/logo.icns" \
            --window-pos 200 120 \
            --window-size 800 400 \
            --icon-size 100 \
            --icon "TuneLab.app" 200 190 \
            --hide-extension "TuneLab.app" \
            --app-drop-link 600 185 \
            "${build_dir}/TuneLab-macOS-${arch}.dmg" \
            "${build_dir}/TuneLab.app"
            
        # 本地签名 DMG
        codesign --force --sign - "${build_dir}/TuneLab-macos-${arch}.dmg"
        
        # 复制说明文件到 DMG 所在目录
        cp "${build_dir}/README.txt" "${build_dir}/TuneLab-macos-${arch}-README.txt"
    else
        print_warning "跳过 DMG 创建步骤"
    fi
}

# 主函数
main() {
    print_info "开始打包流程..."
    
    check_requirements
    clean_build
    
    # 构建 x64 版本
    build_app "x64"
    create_dmg "x64"
    
    # 构建 arm64 版本
    build_app "arm64"
    create_dmg "arm64"
    
    print_info "打包完成！"
    print_info "输出文件位置:"
    print_info "- x64 版本: build/x64/TuneLab.app"
    print_info "- arm64 版本: build/arm64/TuneLab.app"
    
    if [ -f "build/x64/TuneLab-macOS-x64.dmg" ]; then
        print_info "DMG 文件位置:"
        print_info "- x64 版本: build/x64/TuneLab-macOS-x64.dmg"
        print_info "- arm64 版本: build/arm64/TuneLab-macOS-arm64.dmg"
    fi
    
    print_info "每个版本都包含了 README.txt 文件，其中包含首次运行的说明"
}

# 执行主函数
main 