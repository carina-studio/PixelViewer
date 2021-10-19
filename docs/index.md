[![Download](https://img.shields.io/github/v/release/carina-studio/PixelViewer?include_prereleases&style=for-the-badge&color=blue&logo=Windows&label=Windows(Preview))](https://github.com/carina-studio/PixelViewer/releases/download/1.99.0.1019/PixelViewer-1.99.0.1019-win-x64.zip)
[![Download](https://img.shields.io/github/v/release/carina-studio/PixelViewer?include_prereleases&style=for-the-badge&color=blueviolet&logo=Apple&label=macOS(Preview))](https://github.com/carina-studio/PixelViewer/releases/download/1.99.0.1019/PixelViewer-1.99.0.1019-osx-x64.zip)
[![Download](https://img.shields.io/github/v/release/carina-studio/PixelViewer?include_prereleases&style=for-the-badge&color=orange&logo=Linux&logoColor=ffffff&label=Linux(Preview))](https://github.com/carina-studio/PixelViewer/releases/download/1.99.0.1019/PixelViewer-1.99.0.1019-linux-x64.zip)

[![Download](https://img.shields.io/github/v/release/carina-studio/PixelViewer?style=for-the-badge&color=blue&logo=Windows&label=Windows)](https://github.com/carina-studio/PixelViewer/releases/download/1.0.0.617/PixelViewer-1.0.0.617-win-x64.zip)
[![Download](https://img.shields.io/github/v/release/carina-studio/PixelViewer?style=for-the-badge&color=orange&logo=Linux&logoColor=ffffff&label=Linux)](https://github.com/carina-studio/PixelViewer/releases/download/1.0.0.617/PixelViewer-1.0.0.617-linux-x64.zip)

[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/releases/tag/1.99.0.1019)
[![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/commits/master)
[![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB pixels data from file and rendering it.

<img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Dark_Thumb.png" alt="Main window (dark)" width="250"/> <img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Light_Thumb.png" alt="Main window (light)" width="250"/> <img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Dark_Linux_Thumb.png" alt="Main window on Ubuntu (dark)" width="250"/>

## ⭐Supported formats
* Luminance
  * L8
  * L16
* YUV
  * YUV444p
  * P410 (v1.99+)
  * P416 (v1.99+)
  * YUV422p
  * P210 (v1.99+)
  * P216 (v1.99+)
  * UYVY
  * YUVY
  * NV12
  * NV21
  * Y010 (v1.99+)
  * Y016 (v1.99+)
  * I420
  * YV12
  * P010 (v1.99+)
  * P016 (v1.99+)
* RGB
  * BGR_888
  * RGB_565
  * RGB_888
  * BGRX_8888
  * RGBX_8888
  * XBGR_8888
  * XRGB_8888
* ARGB
  * ARGB_8888
  * ABGR_8888
  * BGRA_8888
  * RGBA_8888
* Bayer
  * BGGR_16
  * GBRG_16
  * GRBG_16
  * RGGB_16

## ⭐Supported functions
* Rendering image from raw pixel file.
* Evaluate image dimensions according to file name, file size and format.
* Specify pixel-stride and row-stride for each plane.
* Specify data offset to image in file. (v1.99+)
* Rotate and scale rendered image.
* Navigate to specific image frame in file. (v1.99+)
* Save rendered image as PNG file.

## 💻Installation
Currently PixelViewer is built as portable package, you can just unzip the package and run PixelViewer executable directly without installing .NET runtime environment.

### macOS User
If you want to run PixelViewer on macOS, please do the following steps first:
1. Grant execution permission to PixelViewer. For ex: run command ```chmod 755 PixelViewer``` in terminal.
2. Right click on PixelViewer > ```Open``` > Click ```Open``` on the pop-up window.

You may see that system shows message like ```"XXX.dylib" can't be opened because Apple cannot check it for malicious software``` when you trying to launch PixelViewer. Once you encounter such problem, please follow the steps:
1. Open ```System Preference``` of macOS.
2. Choose ```Security & Privacy``` > ```General``` > Find the blocked library on the bottom and click ```Allow Anyway```.
3. Try launching PixelViewer again.
4. Repeat step 1~3 until all libraries are allowed. 

### Ubuntu user
Some functions of PixelViewer depend on ```libgdiplus``` before v1.99, you may need to install ```libgdiplus``` manually to let PixelViewer runs properly:

```
apt-get install libgdiplus
```

If you want to run PixelViewer on Ubuntu (also for other Linux distributions), please grant execution permission to PixelViewer first. If you want to create an entry on desktop, please follow the steps:
1. Create a file *(name)*.desktop in ~/.local/share/applications. ex, ~/.local/share/applications/pixelviewer.desktop.
2. Open the .desktop file and put the following content:

```
[Desktop Entry]  
Name=PixelViewer  
Comment=  
Exec=(path to executable)
Icon=(path to AppIcon_128px.png in PixelViewer folder)
Terminal=false  
Type=Application
```

3. After saving the file, you should see the entry shown on desktop or application list.

Reference: [How can I edit/create new launcher items in Unity by hand?
](https://askubuntu.com/questions/13758/how-can-i-edit-create-new-launcher-items-in-unity-by-hand)

## 📦Upgrade

### v1.99+
PixelViewer checks for update periodically when you are using. It will notify you to upgrade once the update found. Alternatively you can click "Check for update" item in the "Other actions" menu on the right hand side of toolbar to check whether the update is available or not.

PixelViewer supports self updating on Windows and Linux, so you just need to click "Update" button and wait for updating completed. For macOS user, you just need to download and extract new package, override all existing files to upgrade.

### v1.0
PixelViewer has no installation package nor auto updater. To upgrade PixelViewer, you just need to extract new package and override all existing files.

## 🤝Dependencies
* [.NET](https://dotnet.microsoft.com/)
* [AppBase](https://github.com/carina-studio/AppBase)
* [AppSuiteBase](https://github.com/carina-studio/AppSuiteBase)
* [Avalonia](https://github.com/AvaloniaUI/Avalonia)
* [NLog](https://github.com/NLog/NLog)
* [NUnit](https://github.com/nunit/nunit)
* [ReactiveUI](https://github.com/reactiveui/ReactiveUI)
