# tools/

这个目录放程序要用的外部组件。编译时会自动把它们复制到程序目录下的 `tools\` 里：

| 文件 | 作用 | 来源 |
|---|---|---|
| `yt-dlp.exe` | 视频解析与下载内核 | <https://github.com/yt-dlp/yt-dlp/releases> |
| `ffmpeg.exe` | 合并音视频、转换格式 | <https://www.gyan.dev/ffmpeg/builds/>（release essentials） |
| `deno.exe` | YouTube 解析必需的 JavaScript 运行时 | <https://github.com/denoland/deno/releases> |

这些 exe 文件体积很大，**不提交到 git**（已写进 `.gitignore`）。运行下面的命令自动下载，脚本会逐个校验 SHA-256：

```powershell
.\scripts\fetch-tools.ps1          # 只下载缺少的组件
.\scripts\fetch-tools.ps1 -Force   # 全部更新为最新版
```
