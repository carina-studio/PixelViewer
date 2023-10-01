 ---
+ Version: 1.4
+ Update: 2023/10/1

This is the User Agreement of PixelViewer which you need to read before you using PixelViewer. The User Agreement may be updated in the future and you can check it on the website of PixelViewer. It means that you have agreed this User Agreement once you start using PixelViewer.


## Scope of User Agreement
PixelViewer is a software based-on Open Source Project. The PixelViewer mentioned after includes **ONLY** the executable files or zipped files which are exact same as the files provided by the following pages:

+ [Website of PixelViewer](https://carinastudio.azurewebsites.net/PixelViewer/)
+ [Project and release pages of PixelViewer on GitHub](https://github.com/carina-studio/PixelViewer)

This User Agreement will be applied when you use PixelViewer 3.0 and any future versions before the version specified in next version of User Agreement.


## File Access
Except for system files, all necessary files of PixelViewer are placed inside the directory of PixelViewer (include directory of .NET Runtime if you installed .NET on your computer). No other file access needed when running PixelViewer without loading image except for the followings:

+ Read **/proc/meminfo** to get physical memory information on **Linux**.
+ Read/Write Temporary directory of system for placing runtime resources.
+ Other necessary file access by .NET or 3rd-Party Libraries.

### File Access When Rendering Image
+ The file which contains raw image data will be opened in **Read** mode.

### File Access When Saving Image
+ The file which raw/encoded image data written to will be opened in **Read/Write** mode.

### File Access When Self Updating
+ Downloaded packages and backed-up application files will be placed inside Temporary directory of system.

Other file access outside from executable of PixelViewer are not dominated by this User Agreement.


## Network Access
PixelViewer will access network in the following cases:

### Application Update Checking
PixelViewer downloads manifest from website of PixelViewer periodically to check whether application update is available or not.

### Self Updating
There are 4 type of data will be downloaded when updating PixelViewer:

+ Manifest of auto updater component to check which auto updater is suitable for self updating.
+ Manifest of PixelViewer to check which update package is suitable for self updating.
+ Package of auto updater.
+ Update package of PixelViewer.

### Taking Memory Snapshot
[dotMemory](https://www.jetbrains.com/dotmemory/) is the main tool for memory usage analysis by Carina Studio. When you start taking memory snapshot first time in debug mode, all necessary files of [dotMemory](https://www.jetbrains.com/dotmemory/) will be downloaded into the directory of PixelViewer.

Other network access outside from executable of PixelViewer are not dominated by this User Agreement.


## External Command Execution
There are some necessary external command execution when running PixelViewer:

+ Run **dotnet** to check the version of .NET installed on device.
+ Run **explorer** to open File Explorer on **Windows**.
+ Run **open** to open Finder on **mscOS**.
+ Run **defaults** to check system language and theme mode on **macOS**.
+ Run **nautilus** or **xdg-open** to open File Manager on **Linux**.
+ Run **gsettings** to check system theme mode on Linux.


## Modification of Your Computer
Except for file access, PixelViewer **WON’T** change the settings of your computer.


## License and Copyright
PixelViewer is an Open Source Project of Carina Studio under [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) license. All icons except for application icon are distributed under [MIT](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE) or [CC 4.0](https://en.wikipedia.org/wiki/Creative_Commons_license) license. Please refer to [MahApps.Metro.IconPacks](https://github.com/MahApps/MahApps.Metro.IconPacks) for more information of icons and its license.
 
Application icon is made by [Freepik](https://www.freepik.com/) from [Flaticon](https://www.flaticon.com/).

Built-in fonts **'Noto Sans SC'** and **'Noto Sans TC'** are distributed under [Open Font License](https://scripts.sil.org/cms/scripts/page.php?site_id=nrsi&id=OFL).
 
License and copyright of images loaded into PixelViewer or saved by PixelViewer is not dominated by this User Agreement. You should take care of the license and copyright of images by yourself.


## Contact Us
If you have any concern of this User Agreement, please create an issue on [GitHub](https://github.com/carina-studio/PixelViewer/issues) or send e-mail to [carina.software.studio@gmail.com](mailto:carina.software.studio@gmail.com).