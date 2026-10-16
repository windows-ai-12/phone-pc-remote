# Phone PC Remote · 远程桥

**用手机远程控制你的 Windows 电脑**——不需要第三方服务，纯局域网直连，代码开源。

- 电脑端：单文件 C# 程序（托盘常驻 + 自绘主窗口 + 内置网页控制台）
- 手机端：Android App（WebView 内嵌控制台）
- 功能：实时画面、触控/指针双模式、完整虚拟键盘、程序启动、文件互传、访问密码

---

## ✨ 功能一览

| 功能 | 说明 |
|---|---|
| 实时画面 | 后台持续截屏缓存，画面延迟低，支持双指缩放/平移 |
| 触控 / 指针模式 | 触控=指哪点哪；指针=虚拟触控板（拖动=移动鼠标，轻点=单击） |
| 右键模式 | 开启后轻点 = 右键 |
| 完整虚拟键盘 | 仿 Windows 屏幕键盘：全字母/数字/符号，Shift/Caps/Ctrl/Alt 粘滞组合 |
| 程序启动 | 一键打开记事本/计算器/此电脑/浏览器，或输入任意程序路径 |
| 文件互传 | 手机浏览电脑磁盘，上传手机文件到电脑、下载电脑文件到手机 |
| 访问密码 | 可选 6 位 PIN，无密码模式=同一网络直接进 |
| 开机自启 | 电脑端软件内一键开启 |

---

## 📦 电脑端（RemoteBridge）

### 编译（需要 .NET Framework 4.0+，Windows 自带）

```bat
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /codepage:65001 /target:winexe ^
  /out:RemoteBridge.exe /win32manifest:app.manifest /win32icon:remote_bridge.ico ^
  /r:System.dll /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  Program.cs
```

### 使用

1. 运行 `RemoteBridge.exe`（托盘常驻，双击图标打开主窗口）
2. 主窗口显示：**内网 IP + 端口（默认 8899）**、当前连接数、访问密码设置
3. 手机连同一个路由器，打开 App 填 IP 即可

### 网页控制台

电脑浏览器打开 `http://<电脑IP>:8899/remote` 也可直接控制（同一局域网）。

---

## 📱 手机端（Android App）

### 编译（需要 JDK 17 + Android SDK 35 + build-tools）

```bash
# 1. aapt2 编译资源
aapt2 compile --dir app/res -o compiled.zip
# 2. 链接资源并生成 R.java
aapt2 link -o base.apk -I android-35/android.jar --manifest app/AndroidManifest.xml --java gen --auto-add-overlay compiled.zip
# 3. 编译 Java
javac -source 8 -target 8 -bootclasspath android-35/android.jar -encoding UTF-8 -d classes @sources.txt
# 4. 打包 dex
java -cp build-tools/lib/d8.jar com.android.tools.r8.D8 --lib android-35/android.jar --release --min-api 21 --output dexout classes.jar
# 5. 把 classes.dex 加进 base.apk，zipalign + apksigner 签名（用你自己的 keystore）
```

> 图标：`app/res/mipmap-*/ic_launcher.png` 换成你自己的图标（48/72/96/144/192 px）。

### 使用

1. 安装 APK，输入电脑 IP（默认 `192.168.1.100:8899`，改成你的）
2. 有密码就填密码，没密码直接连
3. 「进入控制台 · App 内打开」开始遥控

---

## 🔒 安全提示（重要）

- **本工具能完全操作你的电脑**，分享/发布前请务必设置访问密码
- 建议仅在**可信局域网**（家庭/办公 WiFi）内使用；公网使用需自担风险并做好防火墙与密码保护
- 不要用它对未经授权的电脑进行控制

---

## 📄 许可

见 [LICENSE](LICENSE)：**允许学习、研究、个人使用；禁止商用；禁止用于未经授权的远程控制。**

---

## 🙏 说明

本项目由个人基于需求用 AI 辅助完成，欢迎大家提建议、报 bug、贡献代码。
