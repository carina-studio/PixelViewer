# PixelViewer User Agreement
 ---
+ Version: 1.8
+ Update: 2026/7/31

This is the PixelViewer User Agreement which you need to read before using PixelViewer. The User Agreement may be updated in the future and you can check it on the PixelViewer website. It means that you have agreed to this User Agreement once you start using PixelViewer.


## User Agreement Scope
PixelViewer is an open-source project of Carina Studio. The PixelViewer mentioned after includes **ONLY** the executable files or zipped files which are exact same as the files provided by the following pages:

+ [PixelViewer Website](https://carinastudio.azurewebsites.net/PixelViewer/)
+ [PixelViewer project and release pages on GitHub](https://github.com/carina-studio/PixelViewer)

If you build PixelViewer from source code, your use of that build is governed solely by the [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) license, not by this User Agreement.

This User Agreement will apply to PixelViewer 2026.1 and any future versions until the version specified in the next User Agreement update.


## Debug Mode
PixelViewer has built-in Debug Mode which is disabled by default. You can enable Debug Mode through **About PixelViewer > Restart in Debug Mode**.


## File Access
Except for system files, all necessary files of PixelViewer are placed inside the PixelViewer directory (including the .NET Runtime directory if you installed .NET on your computer). On **macOS**, due to app signing requirements, app data is stored in the **Application Support** directory (`~/Library/Application Support/CarinaStudio/PixelViewer/`) rather than inside the app bundle. On **Windows** and **Linux**, app data is stored in the application directory itself. No other file access needed when running PixelViewer without loading image except for the followings:

+ Read **/proc/meminfo** to get physical memory information on Linux.
+ Read/Write system Temporary directory for placing runtime resources.
+ Other necessary file access by .NET or 3rd-Party Libraries.

### File Access When Rendering Image
+ The file which contains raw image data will be opened in **Read** mode.

### File Access When Saving Image
+ The file which raw/encoded image data written to will be opened in **Read/Write** mode.

Other file access outside of the PixelViewer executable are not dominated by this User Agreement.


## Network Access
PixelViewer will access network in the following cases:

### Network Connection Check
PixelViewer contacts with the following servers to check network connection:

+ [Cloudflare](https://www.cloudflare.com/)
+ [Google DNS](https://dns.google/)
+ [OpenDNS](https://www.opendns.com/)

PixelViewer contacts with the following servers to check public [IP address](https://en.wikipedia.org/wiki/IP_address) of device:

+ [https://ipv4.icanhazip.com](https://ipv4.icanhazip.com/)
+ [http://checkip.dyndns.org](http://checkip.dyndns.org/)

### Taking Memory Snapshot
[dotMemory](https://www.jetbrains.com/dotmemory/) is the main tool for memory usage analysis by Carina Studio. When you start taking a memory snapshot for the first time in debug mode, all necessary files of [dotMemory](https://www.jetbrains.com/dotmemory/) will be downloaded into the PixelViewer directory.

Other network access outside of the PixelViewer executable are not dominated by this User Agreement.


## External Command Execution
There are some necessary external command execution when running PixelViewer:

+ Run **dotnet** to check the version of .NET installed on device.
+ Run **explorer** to open File Explorer on Windows.
+ Run **open** to open Finder on macOS.
+ Run **defaults** to check system language and theme mode on macOS.
+ Run **nautilus** or **xdg-open** to open File Manager on Linux.
+ Run **gsettings** to check system theme mode on Linux.


## Modification of Your Computer
Except for file access, PixelViewer **WON'T** change the settings of your computer.


## Disclaimer
PixelViewer is provided **"AS IS"** without warranty of any kind, express or implied, including but not limited to the warranties of merchantability, fitness for a particular purpose, and non-infringement. Carina Studio makes no warranty that PixelViewer will meet your requirements or that its operation will be uninterrupted or error-free.

To the fullest extent permitted by applicable law, in no event shall Carina Studio be liable for any direct, indirect, incidental, special, exemplary, or consequential damages (including but not limited to loss of data, loss of profits, or business interruption) arising out of or in connection with the use or inability to use PixelViewer, even if advised of the possibility of such damages.


## License and Copyright
PixelViewer is an open-source project of Carina Studio under [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) license. All icons are distributed under [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE), [CC 4.0](https://en.wikipedia.org/wiki/Creative_Commons_license) or [Universal Multimedia License Agreement for Icons8](https://intercom.help/icons8-7fb7577e8170/en/articles/5534926-universal-multimedia-licensing-agreement-for-icons8) license. Please refer to [MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks), [SVG Repo](https://www.svgrepo.com/), [Icons8](https://icons8.com/), [Google Fonts Icons](https://fonts.google.com/icons), [Phosphor Icons](https://phosphoricons.com/) and [Tabler Icons](https://tabler.io/icons) for more information of icons and their licenses.

Built-in fonts **'Noto Sans SC'** and **'Noto Sans TC'** are distributed under [Open Font License](https://scripts.sil.org/cms/scripts/page.php?site_id=nrsi&id=OFL).

License and copyright of images loaded into PixelViewer or saved by PixelViewer is not dominated by this User Agreement. You should take care of the license and copyright of images by yourself.


## Contact Us
If you have any concern about this User Agreement, please create an issue on [GitHub](https://github.com/carina-studio/PixelViewer/issues) or send e-mail to [carina.software.studio@gmail.com](mailto:carina.software.studio@gmail.com).
