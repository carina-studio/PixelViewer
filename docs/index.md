[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/releases/tag/1.104.0.1123) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat-square)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

PixelViewer is a [.NET](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixels data from file and rendering it.

## 📥 Download

Operating System                      | Download | Version | Screenshot
:------------------------------------:|:--------:|:-------:|:----------:
Windows 8/10/11                       |[x86](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-win-x86.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-win-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-win-arm64.zip)|1.106.0.222 (Preview)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows_Thumb.png" alt="Main window (Windows)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows.png)
Windows 7<br/>*(.NET Runtime needed)* |[x86](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-win-x86-fx-dependent.zip) &#124; [x64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-win-x64-fx-dependent.zip)|1.106.0.222 (Preview)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7_Thumb.png" alt="Main window (Windows 7)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Windows7.png)
macOS 11/12                           |[x64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-osx-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-osx-arm64.zip)|1.106.0.222 (Preview)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS_Thumb.png" alt="Main window (macOS)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_macOS.png)
Linux                                 |[x64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-linux-x64.zip) &#124; [arm64](https://github.com/carina-studio/PixelViewer/releases/download/1.106.0.222/PixelViewer-1.106.0.222-linux-arm64.zip)|1.106.0.222 (Preview)|[<img src="https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora_Thumb.png" alt="Main window (Fedora)" width="150"/>](https://carina-studio.github.io/PixelViewer/Screenshot_MainWindow_Fedora.png)

- [How to Install and Upgrade PixelViewer](installation_and_upgrade.md)

## 📣 What's Change in 1.106.0.222
- Add ```Display-P3``` color space and set as default screen color space on ```macOS```.
- Add information bar to show ```ARGB```/```L*a*b*```/```XYZ``` colors of selected pixel.
- Limited support of parsing DNG file.
- Support saving image with orientation.
- Support 4x4 Bayer Patterns.
- Support RGB gain for ```Bayer Pattern``` formats.
- Support ```BGR_161616```/```RGB_161616``` formats.
- Allow specifying transformation function of brightness/contrast adjustment.
- Add highlight/shadow adjustment on image.
- Support selecting image format automatically according to file name of image.
- Support specifying default byte ordering.
- Support arranging tabs by dragging (not supported yet on ```Linux```).
- Support opening tab in new window.
- Support drag and drop multiple files to open.
- Support layouting windows in horizontal, vertical or tile mode.
- Support using system theme mode on ```macOS```.
- Support hiding rendering parameters panel to get more space for viewing image.
- Support auto update on ```macOS```.
- Improve tab scrolling UX.
- Improve toolbar scrolling UX.
- Calculate memory usage of rendered images with more accuracy.
- Reduce size of update packages for auto updating.
- Other UX improvement.
- Use system menu bar on ```macOS```.
- Enable ```Color Space Management``` by default on ```macOS```.
- Use ```Command``` key instead of ```Ctrl``` key on ```macOS```.
- Use single process to manage all windows.
- Update dependent libraries.
- Correct parameters of ```DCI-P3``` color space.
- Correct algorithm of demosaicing.
- Fix issue of unable to launch on ```ARM64``` PC.
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
