# 白云歌 BaiYunGe

白云歌是 Windows 10/11 x64 离线语音快速输入软件。按住自定义快捷键说话，松开后由本机
模型识别，并将文本输入当前焦点；当前窗口不可输入时复制到剪贴板。软件默认静默运行在
系统托盘，右键托盘图标可打开设置。

> 本仓库为 **v2.0.0 全新重写版**：原生 C# WPF，无浏览器内核，仅 NAudio 一个第三方依赖，
> 后台常驻以轻量化为目标。

## 核心链路

```
按住唤醒键 → WASAPI 录音 → 能量 VAD → llama-server 离线识别 → 词典替换 → 输入/剪贴板
```

除模型下载外不访问网络；识别音频与文本不离开本机；服务器只监听 `127.0.0.1` + 动态端口。

## 构建

环境：Windows 10/11 x64 + .NET 8 SDK。

```powershell
dotnet test BaiYunGe.sln -c Release
dotnet publish src/BaiYunGe/BaiYunGe.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

免安装运行：`artifacts\publish\BaiYunGe.exe`，用 `--data-dir <目录>` 指定运行数据目录。

## 模型

默认模型为 Qwen3-ASR-1.7B Q8_0 主模型 + mmproj 投影模型。安装器不包含模型，可在软件的
「本地模型」页一键下载（仅访问 hf-mirror / huggingface 白名单镜像，校验大小 + SHA-256）。

## 数据目录

- 默认数据目录：`D:\Users\<当前用户>\BaiYunGe`（不占 C 盘）
- 日志 / 配置 / 模型 / 临时音频均在该目录下
- 无可用 D 盘时回退到程序目录 `.data`

## 调试参数

```text
--settings        启动时打开设置
--data-dir <目录>  指定运行数据目录
```

## 技术要点

- UI：.NET 8 WPF（原生，无浏览器内核），暗色实色主题，中英双语
- 音频：NAudio WASAPI 共享模式，任意格式统一转 16kHz/16bit/单声道
- 推理：内置 llama.cpp `llama-server.exe`，Vulkan GPU 优先 + CPU 回退，仅 loopback
- 输入：SendInput Unicode 优先，焦点不可编辑时复制剪贴板
- 版本：2.0.0
