[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/releases/tag/1.104.0.1123) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixels data from file and rendering it.

## 📥 Download

Operating System                      | Download | Version | Screenshot
:------------------------------------:|:--------:|:-------:|:----------:
Windows 8/10/11                       |[x86](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-win-x86.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-win-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-win-arm64.zip)|2.0.3.325|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows_Thumb.png" alt="Main window (Windows)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows.png)
Windows 7<br/>*(.NET Runtime needed)* |[x86](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-win-x86-fx-dependent.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-win-x64-fx-dependent.zip)|2.0.3.325|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7_Thumb.png" alt="Main window (Windows 7)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7.png)
macOS 11/12                           |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-osx-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-osx-arm64.zip)|2.0.3.325|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS_Thumb.png" alt="Main window (macOS)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS.png)
Linux                                 |[x64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-linux-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/2.0.3.325/PixelViewer-2.0.3.325-linux-arm64.zip)|2.0.3.325|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora_Thumb.png" alt="Main window (Fedora)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora.png)

- [How to Install and Upgrade PixelViewer](installation_and_upgrade.md)

## 📣 What's Change in 2.0
- Support 19 new image formats.
- Support multiple image frames in single source file.
- Support demosaicing for Bayer Pattern format.
- Support color space management.
- Add brightness/contrast/highlight/shadow/saturation/vibrance adjustment.
- Add color balance adjustment.
- Add R/G/B gain adjustment.
- Support showing R/G/B and luminance histograms.
- Support saving image as JPEG or raw BGRA pixels.
- UX improvement.
- Update dependent libraries.
- Bug fixing.

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
* Bayer Pattern
  * 10-bit MIPI
  * 12-bit MIPI
  * 16-bit
  
## ⭐ Supported color spaces
* sRGB
* DCI-P3
* Display-P3
* Adobe RGB
* ITU-R BT.601 525-lines
* ITU-R BT.601 625-lines
* ITU-R BT.2020

## ⭐ Supported functions
* Rendering image from raw pixel file.
* Evaluate image dimensions according to file name, file size and format.
* Specify pixel-stride and row-stride for each plane.
* Specify data offset to image in file.
* Specify color space of image and screen.
* Rotate and scale rendered image.
* Navigate to specific image frame in file.
* Adjust brightness/contrast and color balance.
* Adjust highlight/shadow of image.
* Show histograms of R/G/B and luminance.
* Demosaicing for Bayer Pattern formats.
* Save rendered image as PNG file.
* Save rendered image as JPEG/BGRA file.

## 📜 User Agreement
- [English](user_agreement.md)
- [正體中文 (台灣)](user_agreement_zh-TW.md)

## 📜 Privacy Policy
- [English](privacy_policy.md)
- [正體中文 (台灣)](privacy_policy_zh-TW.md)
