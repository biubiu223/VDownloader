# 更新日志

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [1.0.1] - 2026-10-11

### 安全

- B站登录信息改为用 Windows 数据保护（DPAPI）**加密保存**（`bilibili_cookies.dat`）。文件被拷到其他电脑或其他 Windows 账户上无法解密。
- 从 1.0.0 升级时，旧版的明文 `bilibili_cookies.txt` 会自动转成加密文件，并先用 0 覆盖再删除明文。
- 下载时临时解密给 yt-dlp 使用的 Cookie 副本，用完立即覆盖删除；程序启动时还会清理异常退出残留的副本。
- 退出登录时，登录信息改为先覆盖再删除。

### 新增

- 支持用环境变量 `VDOWNLOADER_DATA_DIR` 自定义数据目录。

## [1.0.0] - 2026-10-11

第一个正式版本。

### 新增

- 原生 WinUI 3 界面：Mica 背景、左侧导航，和 WinUI 3 Gallery 同一种风格；支持浅色和深色主题。
- 基于 yt-dlp 内核，支持 B站、YouTube 以及另外 1800 多个网站。
- 画质预设：最高画质、4K、2K、1080p、720p、480p、仅音频（M4A / MP3）。
- 编码偏好：“画质优先”或“兼容优先（H.264 / SDR）”；视频格式可选 MP4 或 MKV。
- “解析并选择画质”：列出全部清晰度、编码、HDR 信息和预估大小，可以单独指定音轨。
- B站官方扫码登录：可解锁 1080P+、4K、HDR、杜比视界、8K（后几项需大会员）。退出登录时会先让服务器端的登录失效，再删除本机凭据。
- 下载队列：并发数可调，支持断点续传、重试、停止；实时显示进度、速度和剩余时间。
- 智能识别链接：B站分享文案、BV 号、不带 http 的网址都能识别；复制链接后切回窗口会自动提示下载。
- 合集 / 播放列表下载，自动放进以合集命名的文件夹。
- 字幕 / 弹幕下载、嵌入封面。
- 代理设置（默认跟随系统代理），国内网站默认直连。
- 其他网站的 Cookie：可以从浏览器读取，也可以导入 cookies.txt。
- 组件管理：内置 yt-dlp、FFmpeg、Deno；可以一键更新 yt-dlp。
- 下载完成时弹出系统通知；启动时检查 GitHub 上的新版本。
- 安装包（Inno Setup，简体中文 / 英文）和绿色版 zip。

[1.0.1]: https://github.com/biubiu223/VDownloader/releases/tag/v1.0.1
[1.0.0]: https://github.com/biubiu223/VDownloader/releases/tag/v1.0.0
