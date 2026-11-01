# Changes in PixelViewer 2026.2
 ---

## New Features
+ Added support for `YUV420 (8-bit, MIPI)`, `YUV420 (Legacy 8-bit, MIPI)`, `YUV420 (10-bit, MIPI)` and `YUV422 (10-bit, MIPI)` formats.
+ Added `ProPhoto RGB` color space.

## Improvement
+ Improved the overall performance of the user interface.
+ Improved the user experience of full screen mode on macOS.

## Behavior Changes
+ Color pre-correction defined by an image file (such as DNG) is no longer kept after opening another file.
+ Color pre-correction of the current profile is applied again when opening a file.

## Bug Fixing
+ Minor bug fixing.