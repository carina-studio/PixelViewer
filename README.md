# PixelViewer [![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/releases/tag/2.0.3.325) ![](https://img.shields.io/github/downloads/carina-studio/PixelViewer/total) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixels data from file and rendering it.

## 📥 Download

### Stable
Operating System                      | Download | Version | Screenshot
:------------------------------------:|:--------:|:-------:|:----------:
Windows 8/10/11                       |[x86](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-x86.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-arm64.zip)|2.7.4.312|[<img src="https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_Windows_Thumb.png" alt="Main window (Windows)" width="150"/>](https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_Windows.png)
macOS 11/12                           |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-osx-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-osx-arm64.zip)|2.7.4.312|[<img src="https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_macOS_Thumb.png" alt="Main window (macOS)" width="150"/>](https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_macOS.png)
Linux                                 |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-linux-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-linux-arm64.zip)|2.7.4.312|[<img src="https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_Fedora_Thumb.png" alt="Main window (Fedora)" width="150"/>](https://carinastudio.azurewebsites.net/PixelViewer/Screenshots/MainWindow_Fedora.png)

- [Installation and Upgrade Guide](https://carinastudio.azurewebsites.net/PixelViewer/InstallAndUpgrade)

## ⭐ Supported formats
* Luminance
  * L8
  * L16
* YUV
  * YUV444p
  * P410
  * P412
  * P416
  * YUV422p
  * P210
  * P212
  * P216
  * UYVY
  * YUVY
  * NV12
  * NV21
  * Y010
  * Y016
  * I420
  * YV12
  * P010
  * P012
  * P016
* RGB
  * BGR_888
  * RGB_565
  * RGB_888
  * BGRX_8888
  * RGBX_8888
  * XBGR_8888
  * XRGB_8888
  * BGR_161616
  * RGB_161616
* ARGB
  * ARGB_8888
  * ABGR_8888
  * BGRA_8888
  * RGBA_8888
  * ARGB_16161616
  * ABGR_16161616
  * BGRA_16161616
  * RGBA_16161616
  * ABGR_2101010
  * ARGB_2101010
  * BGRA_1010102
  * RGBA_1010102
  * ABGR_F16
  * ARGB_F16
  * BGRA_F16
  * RGBA_F16
* Bayer Pattern
  * 10-bit MIPI
  * 12-bit MIPI
  * 16-bit
* Compressed
  * HEIF
  * JPEG/JFIF
  * PNG
  
## ⭐ Supported color spaces
* sRGB
* DCI-P3
* Display-P3
* Adobe RGB
* ITU-R BT.601 525-lines
* ITU-R BT.601 625-lines
* ITU-R BT.2020
* ITU-R BT.2100 (HLG)
* ITU-R BT.2100 (PQ)

## ⭐ Supported functions
* Rendering image from raw pixel file.
* Evaluate image dimensions according to file name, file size and format.
* Specify pixel-stride and row-stride for each plane.
* Specify data offset to image in file.
* Specify color space of image and screen.
* Import ICC profile as custom color space.
* Rotate and scale rendered image.
* Navigate to specific image frame in file.
* Adjust R/G/B gain for Bayer Pattern formats.
* Adjust brightness/contrast and color balance.
* Adjust highlight/shadow of image.
* Show histograms of R/G/B and luminance.
* Demosaicing for Bayer Pattern formats.
* Save rendered image as PNG file.
* Save rendered image as JPEG/BGRA file.

## 🤝 Dependencies
* [.NET](https://dotnet.microsoft.com/)
* [AppBase](https://github.com/carina-studio/AppBase)
* [AppSuiteBase](https://github.com/carina-studio/AppSuiteBase)
* [Avalonia](https://github.com/AvaloniaUI/Avalonia)
* [ExifLibNet](https://github.com/oozcitak/exiflibrary)
* [Magick.NET](https://github.com/dlemstra/Magick.NET)
* [NLog](https://github.com/NLog/NLog)
* [NUnit](https://github.com/nunit/nunit)
* [SharpZipLib](https://github.com/icsharpcode/SharpZipLib)
