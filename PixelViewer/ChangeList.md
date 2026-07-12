# Changes in PixelViewer 2026
 ---

## New Features
+ Added support for shutting down the application by keyboard shortcut (`Ctrl+Q` on Windows/Linux, `⌘Q` on macOS).
+ Added support for opening TIFF images.
+ Added support for saving an image as TIFF.

## Improvement
+ Added support for specifying the effective bits of images with `ABGR_16161616`, `ARGB_16161616`, `BGRA_16161616` and `RGBA_16161616` formats.
+ Improved the performance of saving images in PNG format.
+ Added support for adjusting the width of the panel of histograms.

## Behavior Changes
+ 

## Bug Fixing
+ Fixed the incorrect color value range of the selected pixel shown for images with RGB/ARGB formats such as `BGRA_8888` and `RGB_565`.
+ Fixed the failure to detect the image file format when it cannot be identified from the file name (e.g. a file with an incorrect extension).
+ Fixed the failure to load some ICC color profiles which caused the screen color space of certain wide-gamut displays to be incorrectly detected as sRGB.
+ Minor bug fixing.