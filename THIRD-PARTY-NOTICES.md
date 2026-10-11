# 第三方组件说明 / Third-Party Notices

万能视频下载器（VDownloader）本身以 [GPL-3.0](LICENSE) 许可证发布。
发布包里还附带了下面这些第三方组件，它们各自遵循自己的许可证，版权归原作者所有。

## 随程序分发的组件

| 组件 | 用途 | 许可证 | 项目地址 |
|---|---|---|---|
| **yt-dlp** | 视频解析与下载内核（`tools\yt-dlp.exe`） | [The Unlicense](https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE)（公有领域） | <https://github.com/yt-dlp/yt-dlp> |
| **FFmpeg**（gyan.dev essentials 静态编译版） | 合并音视频、转换格式（`tools\ffmpeg.exe`） | [GPL-3.0](https://www.ffmpeg.org/legal.html)（这个编译版启用了 GPL 组件） | <https://ffmpeg.org> · 编译版：<https://www.gyan.dev/ffmpeg/builds/> |
| **Deno** | YouTube 解析所需的 JavaScript 运行时（`tools\deno.exe`） | [MIT](https://github.com/denoland/deno/blob/main/LICENSE.md) | <https://github.com/denoland/deno> |
| **.NET 8 运行时** | 程序运行环境 | [MIT](https://github.com/dotnet/runtime/blob/main/LICENSE.TXT) | <https://github.com/dotnet/runtime> |
| **Windows App SDK / WinUI 3** | 界面框架运行库 | [Microsoft 软件许可条款（可再分发）](https://www.nuget.org/packages/Microsoft.WindowsAppSDK) | <https://github.com/microsoft/WindowsAppSDK> |
| **QRCoder** | 生成 B站登录二维码 | [MIT](https://github.com/codebude/QRCoder/blob/master/LICENSE.txt) | <https://github.com/codebude/QRCoder> |

### FFmpeg 源代码

`ffmpeg.exe` 是 gyan.dev 编译的 FFmpeg，未经修改。按照 GPL 的要求，它的源代码可以从这些地方获取：

- FFmpeg 官方源码：<https://ffmpeg.org/download.html#get-sources>
- 该编译版的版本信息与所用源码：<https://www.gyan.dev/ffmpeg/builds/>

## 只在制作安装包时使用的工具

| 工具 | 许可证 | 项目地址 |
|---|---|---|
| **Inno Setup**（含简体中文语言文件，由 Zhenghan Yang 维护） | [Inno Setup License](https://jrsoftware.org/files/is/license.txt) | <https://jrsoftware.org/isinfo.php> |

## 其他说明

- 界面图标使用 Windows 系统自带的 Segoe Fluent Icons 字体，它不随本软件分发。
- “B站”“哔哩哔哩”“YouTube” 等名称及商标归各自所有者。本项目与这些网站没有任何关联。
