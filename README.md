# PixelViewer
PixelViewer is a [.NET Core](https://dotnet.microsoft.com/) based cross-platform image viewer written by C# which supports reading raw Luminance/YUV/RGB/ARGB pixels data from file and rendering it. Currently it is still under development, but most of functions are ready.

<img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Dark_Thumb.png" alt="Main window (dark)" width="250"/> <img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Light_Thumb.png" alt="Main window (light)" width="250"/> <img src="https://carina-studio.github.io/PixelViewer/Screenshot_Main_Dark_Linux_Thumb.png" alt="Main window on Ubuntu (dark)" width="250"/>

## Supported formats
* Luminance
  * L8
  * L16 (LE/BE)
* YUV
  * YUV444p
  * YUV422p
  * NV12
  * NV21
  * I420
  * YV12
* RGB
  * BGR_888
  * RGB_565 (LE/BE)
  * RGB_888
* ARGB
  * ARGB_8888
  * ABGR_8888
  * BGRA_8888
  * RGBA_8888

## Supported functions
* Rendering image from raw pixel file.
* Evaluate image dimensions according to file name, file size and format.
* Specify pixel-stride and row-stride for each plane.
* Rotate and scale rendered image.
* Save rendered image as PNG file.

## Download
You can find and download all releases here: [Releases](https://github.com/carina-studio/PixelViewer/releases)

## Installation
Currently PixelViewer is built as portable package, you can just unzip the package and run PixelViewer executable directly without installing .NET Core runtime environment.
### Ubuntu user
If you want to run PixelViewer on Ubuntu (also for other Linux distributions), please grant execution permission to PixelViewer first. If you want to create an entry on desktop, please follow the following steps:
1. Create a file *(name)*.desktop in ~/.local/share/applications. ex, ~/.local/share/applications/pixelviewer.desktop.
2. Open the .desktop file and put the following content:

> [Desktop Entry]  
> Name=PixelViewer  
> Comment=  
> Exec=*(path to executable)*  
> Icon=*(path to AppIcon_128px.png in PixelViewer folder)*  
> Terminal=false  
> Type=Application

3. After saving the file, you should see the entry shown on desktop or application list.

Reference: [How can I edit/create new launcher items in Unity by hand?
](https://askubuntu.com/questions/13758/how-can-i-edit-create-new-launcher-items-in-unity-by-hand)
