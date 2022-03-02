---
title: PixelViewer
---

# How to Install and Upgrade PixelViewer

## 💻Installation
PixelViewer is built as portable package. Except for Windows 7, you can just unzip the package and run PixelViewer executable directly without installing .NET Runtime.

### Windows 7 User
You need to install [.NET Desktop Runtime 6.0.1+](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) before running PixelViewer.

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
Icon=(path to PixelViewer.png in PixelViewer folder)
Terminal=false  
Type=Application
```

3. After saving the file, you should see the entry shown on desktop or application list.

Reference: [How can I edit/create new launcher items in Unity by hand?
](https://askubuntu.com/questions/13758/how-can-i-edit-create-new-launcher-items-in-unity-by-hand)

Currently PixelViewer lacks of ability to detect screen DPI on Linux, so you may find that UI displays too small on Hi-DPI screen. In this case you can open ```Application Options``` of PixelViewer, find ```User interface scale factor``` and change the scale factor to proper value. If you found that scale factor doesn't work on your Linux PC, please install ```xrandr``` tool then check again.

## 📦Upgrade

### v1.99+
PixelViewer checks for update periodically when you are using. It will notify you to upgrade once the update found. Alternatively you can click ```Check for update``` item in the ```Other actions``` menu on the right hand side of toolbar to check whether the update is available or not.

PixelViewer supports self updating, so you just need to click ```Update``` button and wait for updating completed. If you are using PixelViewer before 1.106.0.222 on macOS, you just need to download and extract new package, override all existing files to upgrade.

### v1.0
PixelViewer has no installation package nor auto updater. To upgrade PixelViewer, you just need to extract new package and override all existing files.

<br/>📔[Back to Home](index.md)
