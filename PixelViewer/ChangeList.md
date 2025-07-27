# What's Change in PixelViewer 3.1
 ---

## New Features
+ Add **Bayer Pattern (14-bit, MIPI)**, **YUYV (YUV422)** and **YVYU (YUV422)** formats.

## Improvement
+ Better way to convert from **Bayer Pattern (10-bit, MIPI)** and **Bayer Pattern (12-bit, MIPI)** to 16-bit color.

## Behavior Changes
+ Remove **Y010** and **Y016** formats because they are not standardized by Microsoft.

## Bug Fixing
+ Fix conversion from **Bayer Pattern (10-bit, MIPI)** and **Bayer Pattern (12-bit, MIPI)** to 16-bit color.
+ Fix artifact of demosaicing on edge of Bayer Pattern images.
+ Minor bug fixing.