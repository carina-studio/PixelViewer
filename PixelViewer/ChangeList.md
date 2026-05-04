# Changes in PixelViewer 2026
 ---

## New Features
+ Added support for pinch gesture on touchscreen and touchpad to zoom image.
+ Added support for flipping image horizontally and vertically.
+ Added `ARGB Color Format` setting to display the selected pixel color as `Source Image Bit Depth` (up to 16 bits per channel), `8-Bit (0-255)`, or `Normalized [0.0, 1.0]`.

## Improvement
+ Improved the user experience of image viewer.
+ Improved metadata reading to correctly handle all `EXIF`/`TIFF` orientation values, including those that encode flip.
+ Improved selected pixel color readout to show `RGB(...)` instead of `ARGB(...)` when the source image has no alpha channel.

## Behavior Changes
+ Double-tapping image now toggles between `Fit to Viewport` and `Zoom to 100%`.

## Bug Fixing
+ Minor bug fixing.