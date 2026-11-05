# Changes in PixelViewer 2026.2
 ---

## New Features
+ Added support for `YUV420 (8-bit, MIPI)`, `YUV420 (Legacy 8-bit, MIPI)`, `YUV420 (10-bit, MIPI)`, `YUV422 (10-bit, MIPI)`, `Bayer Pattern (10-bit, Packed)`, `Bayer Pattern (12-bit, Packed)` and `Bayer Pattern (14-bit, Packed)` formats.
+ Added support for color pre-correction of RGB and ARGB formats.
+ Added `ProPhoto RGB` color space.

## Improvement
+ Improved the overall performance of the user interface.
+ Redesigned the rendering parameters panel.
+ Improved the user experience of full screen mode on macOS.
+ Improved the user experience of specifying color pre-correction.
+ Improved the accuracy of color pre-correction.

## Behavior Changes
+ Color pre-correction defined by an image file (such as DNG) is no longer kept after opening another file.
+ Color pre-correction of the current profile is applied again when opening a file, or when selecting a format which does not support it.

## Bug Fixing
+ Fixed small changes of color pre-correction being ignored.
+ Minor bug fixing.