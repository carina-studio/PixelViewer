# PixelViewer 用户协议
 ---
+ 版本：1.8
+ 更新时间：2026/8/21

这是 PixelViewer 的用户协议，您应该要在使用 PixelViewer 之前详细阅读本协议。 用户协议可能会在未来有所更新，您可以在 PixelViewer 网站中查看。 当您开始使用 PixelViewer 表示您同意本用户协议。


## 适用范围
PixelViewer 为 Carina Studio 的开放源代码项目，以下所指 PixelViewer **仅包括** 与下列页面所提供的可执行文件或压缩包内容完全相同的版本：

+ [PixelViewer 网站](https://carinastudio.net/PixelViewer/)
+ [GitHub 上的 PixelViewer 项目页面及各版本发布页面](https://github.com/carina-studio/PixelViewer)

若您通过源代码自行构建 PixelViewer，您使用该构建的版本仅受 [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) 授权约束，不受本用户协议约束。

本用户协议适用于您使用 PixelViewer 2026.1 及下一份用户协议所指定的版本之间 (但不包括) 的所有版本。


## 调试模式
PixelViewer 包含默认关闭的内建调试模式，您可以通过 **“关于 PixelViewer > 以调试模式重新启动”** 启用调试模式。


## 文件访问
除了系统文件之外，所有 PixelViewer 所需的文件皆存放于 PixelViewer 目录内（若您有安装 .NET 则亦包含 .NET 运行期间的目录）。在 **macOS** 上，由于应用程序签名的要求，应用程序数据将存放于 **Application Support** 目录（`~/Library/Application Support/CarinaStudio/PixelViewer/`）而非应用程序包内。在 **Windows** 及 **Linux** 上，应用程序数据存放于应用程序目录本身。当执行 PixelViewer 且未加载任何图像时不需要额外的文件访问，除了下列之外：

+ 读取 **/proc/meminfo** 以在 Linux 上获取内存信息。
+ 读/写系统的临时目录以存放运行期间所需资源。
+ 其余由 .NET 或第三方程序库进行的必要文件访问。

### 图像加载时的文件访问
+ 包含原始图像内容的文件将以 **读取** 模式打开。

### 图像保存时的文件访问
+ 写入图像内容的文件将以 **读写** 模式打开。

其他由 PixelViewer 可执行文件以外的文件访问不受本协议约束。


## 网络访问
PixelViewer 将会在下列状况访问网络：

### 网络连接测试
PixelViewer 会连接至下列服务器以确认网络连接状态：

+ [Cloudflare](https://www.cloudflare.com/)
+ [Google DNS](https://dns.google/)
+ [OpenDNS](https://www.opendns.com/)

PixelViewer 会连接至下列服务器以确认设备的公开 [IP 地址](https://zh.wikipedia.org/wiki/IP%E5%9C%B0%E5%9D%80)：

+ [https://ipv4.icanhazip.com](https://ipv4.icanhazip.com/)
+ [http://checkip.dyndns.org](http://checkip.dyndns.org/)

### 捕获内存快照
[dotMemory](https://www.jetbrains.com/dotmemory/) 是 Carina Studio 用以分析内存使用状况的主要工具。当您第一次在调试模式中捕获内存快照时，所有 [dotMemory](https://www.jetbrains.com/dotmemory/) 所需的文件将下载至 PixelViewer 目录中。

其他由 PixelViewer 可执行文件以外的网络访问不受本协议约束。


## 执行外部命令
在执行 PixelViewer 时有些必要情况需要执行外部命令：

+ 执行 **dotnet** 以确认在设备上安装的 .NET 版本。
+ 执行 **explorer** 以在 Windows 上打开 Windows 资源管理器。
+ 执行 **open** 以在 macOS 上打开 Finder。
+ 执行 **defaults** 以确认在 macOS 上的系统语言与主题设置。
+ 执行 **nautilus** 或 **xdg-open** 以在 Linux 上打开文件管理器。
+ 执行 **gsettings** 以确认在 Linux 上的系统主题设置。


## 修改您的电脑
除了文件访问，PixelViewer **不会** 更改您电脑的设置。


## 免责声明
PixelViewer 系以 **“现状”** 提供，不附带任何明示或暗示的保证，包括但不限于适销性、特定用途适用性及不侵权的保证。Carina Studio 不保证 PixelViewer 能符合您的需求，亦不保证其运行不会中断或不发生错误。

在适用法律允许的最大范围内，Carina Studio 对于因使用或无法使用 PixelViewer 而产生的任何直接、间接、偶发、特殊、惩罚性或衍生性损害（包括但不限于数据丢失、利润损失或业务中断），概不承担任何责任，即使已被告知可能发生此类损害亦然。


## 授权及版权
PixelViewer 是 Carina Studio 在 [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) 授权下的开放源代码项目。所有图标皆在 [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE)、[CC 4.0](https://en.wikipedia.org/wiki/Creative_Commons_license) 或 [Universal Multimedia License Agreement for Icons8](https://intercom.help/icons8-7fb7577e8170/en/articles/5534926-universal-multimedia-licensing-agreement-for-icons8) 授权下使用。您可以在 [MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks)、[SVG Repo](https://www.svgrepo.com/)、[Icons8](https://icons8.com/)、[Google Fonts Icons](https://fonts.google.com/icons)、[Phosphor Icons](https://phosphoricons.com/) 及 [Tabler Icons](https://tabler.io/icons) 了解更多图标相关信息与授权。

内建字体 **“Noto Sans SC”** 及 **“Noto Sans TC”** 在 [Open Font License](https://scripts.sil.org/cms/scripts/page.php?site_id=nrsi&id=OFL) 授权下使用及发布。

加载至 PixelViewer 或由 PixelViewer 保存的图像的授权与版权不受本协议约束。您必须自行注意及负责图像的授权与版权。


## 联系我们
如果您对于本用户协议有任何疑问，可以至 [GitHub](https://github.com/carina-studio/PixelViewer/issues) 提出或发送邮件至 [support@carinastudio.net](mailto:support@carinastudio.net)。
