# PixelViewer 
[![](https://img.shields.io/github/release-date-pre/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/releases/tag/2.7.4.312) ![](https://img.shields.io/github/downloads/carina-studio/PixelViewer/total) [![](https://img.shields.io/github/last-commit/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/commits/master) [![](https://img.shields.io/github/license/carina-studio/PixelViewer?style=flat)](https://github.com/carina-studio/PixelViewer/blob/master/LICENSE.md)

A cross-platform image viewer that supports reading raw Luminance/YUV/RGB/ARGB/Bayer pixel data from files and rendering them. Please visit the [Website](https://carinastudio.net/PixelViewer/) for more details.

跨平台影像檢視器，支援讀取及繪製原始 Luminance/YUV/RGB/ARGB/Bayer 像素資料。請參閱 [網站](https://carinastudio.net/PixelViewer/) 以取得更多資訊。

![](https://carinastudio.net/PixelViewer/Banner-v2.png?v=1)

## ⚠️ NOTICE
If you are upgrading from `3.x` to `2026+` on macOS, please download the application bundle manually and place it alongside the existing `3.x` bundle *(instead of replacing it)*. This allows `2026+` to import your application data from the `3.x` bundle on first launch.

## 📥 Download 2026.1

### Windows
[![](https://img.shields.io/badge/x64-blue?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-win-x64.zip)
[![](https://img.shields.io/badge/arm64-blue?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-win-arm64.zip)

### macOS
[![](https://img.shields.io/badge/Apple%20Silicon%20(arm64)-blueviolet?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-osx-arm64.zip)
[![](https://img.shields.io/badge/x64-blueviolet?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-osx-x64.zip)

### Linux
[![](https://img.shields.io/badge/x64-orange?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-linux-x64.zip)
[![](https://img.shields.io/badge/arm64-orange?style=for-the-badge)](https://packages.carinastudio.net/PixelViewer/2026.1.3/PixelViewer-2026.1.3-linux-arm64.zip)

## ⭐ Supported formats
| Luminance | YUV                 | RGB        | ARGB          | Bayer Pattern | Compressed     |
|-----------|---------------------|------------|---------------|---------------|----------------|
| L8        | YUV444p             | BGR_888    | ARGB_8888     | 10-bit MIPI   | HEIF           |
| L16       | P410                | RGB_565    | ABGR_8888     | 12-bit MIPI   | JPEG/JFIF      |
|           | P416                | RGB_888    | BGRA_8888     | 14-bit MIPI   | PNG            |
|           | YUV422p             | BGRX_8888  | RGBA_8888     | 16-bit        | TIFF `2026.1+` |
|           | P210                | RGBX_8888  | ARGB_16161616 | 8-bit         | WebP           |
|           | P216                | XBGR_8888  | ABGR_16161616 |               |                |
|           | UYVY                | XRGB_8888  | BGRA_16161616 |               |                |
|           | YUVY                | BGR_161616 | RGBA_16161616 |               |                |
|           | YUYV                | RGB_161616 | ABGR_2101010  |               |                |
|           | YVYU                |            | ARGB_2101010  |               |                |
|           | NV12                |            | BGRA_1010102  |               |                |
|           | NV21                |            | RGBA_1010102  |               |                |
|           | I420                |            | ABGR_F16      |               |                |
|           | YV12                |            | ARGB_F16      |               |                |
|           | Android_YUV_420_888 |            | BGRA_F16      |               |                |
|           | P010                |            | RGBA_F16      |               |                |
|           | P016                |            |               |               |                |

## ⭐ Supported color spaces
- sRGB
- DCI-P3
- Display-P3
- Adobe RGB
- ITU-R BT.601 525-lines
- ITU-R BT.601 625-lines
- ITU-R BT.2020
- ITU-R BT.2100 (HLG)
- ITU-R BT.2100 (PQ)

## ⭐ Supported functions
- Rendering image from raw pixel file.
- Evaluate image dimensions according to file name, file size and format.
- Specify pixel-stride and row-stride for each plane.
- Specify data offset to image in file.
- Specify color space of image and screen.
- Import ICC profile as custom color space.
- Defining custom image rendering scripts in JavaScript, C# or Python. `2026.1+`
- Rotate and scale rendered image.
- Navigate to specific image frame in file.
- Playing frames continuously with an adjustable frame rate. `2026.1+`
- Adjust R/G/B gain for Bayer Pattern formats.
- Adjust brightness/contrast and color balance.
- Adjust highlight/shadow of image.
- Show histograms of R/G/B and luminance.
- Demosaicing for Bayer Pattern formats.
- Defining custom demosaicing scripts in JavaScript, C# or Python. `2026.1+`
- Pasting an image from the clipboard. `2026.1+`
- Saving the rendered image as a PNG/JPEG/TIFF/BGRA file.

## 🤝 Dependencies
- [.NET](https://dotnet.microsoft.com/)
- [AppBase](https://github.com/carina-studio/AppBase)
- [AppSuiteBase](https://github.com/carina-studio/AppSuiteBase)
- [Avalonia](https://github.com/AvaloniaUI/Avalonia)
- [AvaloniaEdit](https://github.com/AvaloniaUI/AvaloniaEdit)
- [ExifLibNet](https://github.com/oozcitak/exiflibrary)
- [IronPython 3](https://github.com/IronLanguages/ironpython3)
- [Jint](https://github.com/sebastienros/jint)
- [Magick.NET](https://github.com/dlemstra/Magick.NET)
- [NLog](https://github.com/NLog/NLog)
- [NUnit](https://github.com/nunit/nunit)
- [Roslyn](https://github.com/dotnet/roslyn)
