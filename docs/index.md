[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/releases/tag/1.104.0.1123) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixels data from file and rendering it.

## 📥 Download

Operating System                      | Download | Version | Screenshot
:------------------------------------:|:--------:|:-------:|:----------:
Windows 8/10/11                       |[x86](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-win-x86.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-win-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-win-arm64.zip)|2.0.0.308 (RC)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows_Thumb.png" alt="Main window (Windows)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows.png)
Windows 7<br/>*(.NET Runtime needed)* |[x86](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-win-x86-fx-dependent.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-win-x64-fx-dependent.zip)|2.0.0.308 (RC)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7_Thumb.png" alt="Main window (Windows 7)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7.png)
macOS 11/12                           |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-osx-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-osx-arm64.zip)|2.0.0.308 (RC)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS_Thumb.png" alt="Main window (macOS)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS.png)
Linux                                 |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-linux-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.0.308/PixelViewer-2.0.0.308-linux-arm64.zip)|2.0.0.308 (RC)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora_Thumb.png" alt="Main window (Fedora)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora.png)

- [How to Install and Upgrade PixelViewer](installation_and_upgrade.md)

## 📣 What's Change in 2.0 RC
- Add saturation/vibrance adjustment.
- Add auto color adjustment.
- Add auto R/G/B gain selection.
- Support resetting image filter parameters after opening image file.
- Support specifying default color space of image.
- Support using ```Ctrl/Cmd +/-``` to zoom image when ```Fit to viewport``` is ON.
- Update algorithm of highlight/shadow adjustment.
- Keep latest selected compression quality of JPEG.
- Other UX improvement.
- Update dependent libraries.
- Other bug fixing.

## ⭐ Supported formats
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
  * BGR_161616 (v1.106+)
  * RGB_161616 (v1.106+)
* ARGB
  * ARGB_8888
  * ABGR_8888
  * BGRA_8888
  * RGBA_8888
  * ARGB_16161616 (v1.105+)
  * ABGR_16161616 (v1.105+)
  * BGRA_16161616 (v1.105+)
  * RGBA_16161616 (v1.105+)
* Bayer Pattern
  * 10-bit MIPI (v1.105+)
  * 12-bit MIPI (v1.105+)
  * 16-bit
  
## ⭐ Supported color spaces (v1.104+)
* sRGB
* DCI-P3
* Display-P3 (v1.106+)
* Adobe RGB
* ITU-R BT.601
* ITU-R BT.2020

## ⭐ Supported functions
* Rendering image from raw pixel file.
* Evaluate image dimensions according to file name, file size and format.
* Specify pixel-stride and row-stride for each plane.
* Specify data offset to image in file. (v1.99+)
* Specify color space of image and screen. (v1.104+)
* Rotate and scale rendered image.
* Navigate to specific image frame in file. (v1.99+)
* Adjust brightness/contrast and color balance. (v1.104+)
* Adjust highlight/shadow of image. (v1.106+)
* Show histograms of R/G/B and luminance. (v1.102+)
* Demosaicing for Bayer Pattern formats. (v1.103+)
* Save rendered image as PNG file.
* Save rendered image as JPEG/BGRA file. (v1.105+)

## 📜 User Agreement
- [English](user_agreement.md)
- [正體中文 (台灣)](user_agreement_zh-TW.md)

## 📜 Privacy Policy
- [English](privacy_policy.md)
- [正體中文 (台灣)](privacy_policy_zh-TW.md)
