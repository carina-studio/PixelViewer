# Changes in PixelViewer 2026
 ---

## New Features
+ Added support for shutting down the application by keyboard shortcut (`⌘Q` on macOS, `Ctrl+Q` on Windows/Linux).

## Improvement
+ Added support for specifying the effective bits of images with `ABGR_16161616`, `ARGB_16161616`, `BGRA_16161616` and `RGBA_16161616` formats.

## Behavior Changes
+ 

## Bug Fixing
+ Fixed the incorrect color value range of the selected pixel shown for images with RGB/ARGB formats such as `BGRA_8888` and `RGB_565`.
+ Fixed the failure to detect the image file format when it cannot be identified from the file name (e.g. a file with an incorrect extension).
+ Minor bug fixing.