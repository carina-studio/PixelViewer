# PixelViewer 
[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/releases/tag/2.7.4.312) ![](https://img.shields.io/github/downloads/carina-studio/PixelViewer/total) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixels data from file and rendering it. Please visit the [Website](https://carinastudio.azurewebsites.net/PixelViewer/) for more details.

PixelViewer 是一個由 C# 撰寫並基於 [.NET](https://dotnet.microsoft.com/) 的跨平台影像檢視器，支援讀取及繪製原始 Luminance/YUV/RGB/ARGB/Bayer 像素資料。請參閱 [網站](https://carinastudio.azurewebsites.net/PixelViewer/) 以取得更多資訊。

<img alt="PixelViewer" src="https://carinastudio.azurewebsites.net/PixelViewer/Banner.png"/>

## 📥 Download

### 3.0.3.123 RC
[![](https://img.shields.io/badge/Windows-x64-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-win-x64.zip)
[![](https://img.shields.io/badge/Windows-arm64-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-win-arm64.zip)
[![](https://img.shields.io/badge/Windows-x86-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-win-x86.zip)

[![](https://img.shields.io/badge/macOS-arm64%20(M1/M2)-blueviolet?style=flat-square&logo=apple&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-osx-arm64.zip)
[![](https://img.shields.io/badge/macOS-x64-blueviolet?style=flat-square&logo=apple&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-osx-x64.zip)

[![](https://img.shields.io/badge/Linux-x64-orange?style=flat-square&logo=linux&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-linux-x64.zip)
[![](https://img.shields.io/badge/Linux-arm64-orange?style=flat-square&logo=linux&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/3.0.3.123/PixelViewer-3.0.3.123-linux-arm64.zip)

### 2.7.4.312
[![](https://img.shields.io/badge/Windows-x64-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-x64.zip)
[![](https://img.shields.io/badge/Windows-arm64-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-arm64.zip)
[![](https://img.shields.io/badge/Windows-x86-blue?style=flat-square&logo=windows&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-win-x86.zip)

[![](https://img.shields.io/badge/macOS-arm64%20(M1/M2)-blueviolet?style=flat-square&logo=apple&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-osx-arm64.zip)
[![](https://img.shields.io/badge/macOS-x64-blueviolet?style=flat-square&logo=apple&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-osx-x64.zip)

[![](https://img.shields.io/badge/Linux-x64-orange?style=flat-square&logo=linux&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-linux-x64.zip)
[![](https://img.shields.io/badge/Linux-arm64-orange?style=flat-square&logo=linux&logoColor=fff)](https://github.com/carina-studio/PixelViewer/releases/download/2.7.4.312/PixelViewer-2.7.4.312-linux-arm64.zip)

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
  * 8-bit (v3.0+)
* Compressed
  * HEIF
  * JPEG/JFIF
  * PNG
  * WebP (v3.0+)
  
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
