# Changes in PixelViewer 2026
 ---

## New Features
+ Added support for playing frames continuously with an adjustable frame rate.
+ Added support for opening multiple files as a single frame sequence.
+ Added support for shutting down the application by keyboard shortcut (`Ctrl+Q` on Windows/Linux, `⌘Q` on macOS).
+ Added support for opening TIFF images.
+ Added support for saving an image as TIFF.
+ Added support for showing or hiding the mean value marker on histograms.

## Improvement
+ Added support for parsing and applying the white balance recorded in DNG images.
+ Added support for parsing and applying the color matrix recorded in DNG images.
+ Added support for parsing and applying the linearization table recorded in DNG images.
+ Added support for specifying the effective bits of images with `ABGR_16161616`, `ARGB_16161616`, `BGRA_16161616` and `RGBA_16161616` formats.
+ Improved the performance of demosaicing.
+ Improved the performance of saving images in PNG format.
+ Added support for saving the rendered image while the next image is being rendered.
+ Added support for adjusting the width of the panel of histograms.
+ The name generated for new profile includes the name of camera by default.
+ The application no longer needs to be restarted when the Chinese environment changes after modifying the `Language` option.
+ Prevented the displayed image from being cleared when the memory usage of rendered images reaches the limit.

## Behavior Changes
+ The effective bits of image planes can no longer be specified when a color table is applied to the rendering, such as for a DNG image which carries a linearization table.

## Bug Fixing
+ Fixed the incorrect color value range of the selected pixel shown for images with RGB/ARGB formats such as `BGRA_8888` and `RGB_565`.
+ Fixed the failure to detect the image file format when it cannot be identified from the file name (e.g. a file with an incorrect extension).
+ Fixed the low contrast of some DNG images caused by their black level not being read correctly.
+ Fixed the incorrect colors of DNG images with the `GBRG (4x4)` Bayer pattern.
+ Fixed the failure to load some ICC color profiles which caused the screen color space of certain wide-gamut displays to be incorrectly detected as `sRGB`.
+ Fixed the incorrect colors of images which use the `BT.601 (525-line, SDTV)` or `BT.601 (625-line, SDTV)` color space caused by an incorrect white point.
+ Fixed the color space of an image being incorrectly encoded when saving it as JPEG or PNG, and incorrectly decoded when reopening it, for color spaces such as `BT.2100 (HLG transfer, HDR-TV)` and `BT.2100 (PQ transfer, HDR-TV)`.
+ Fixed the failure to use `Noto Sans` in the Chinese environment.
+ Fixed the image not being rendered when requesting rendering again before the current rendering completes.
+ Fixed the `Treat as Linear Color Space` option being turned off automatically after saving the rendering profile.
+ Minor bug fixing.