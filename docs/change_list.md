---
title: PixelViewer
---

## 📣 What's Change in 2.6 Preview
- Add ```Treat as linear color space``` option to enable color space conversion without numerical linear transfer.
- Improve performance of color space conversion.
- Remove ```Linear sRGB``` color space.
- Cancel taking luminance into account for RGB gain selection.
- Other bug fixing.

## 📣 What's Change in 2.5 Preview
- Support importing ICC profile to create custom color spaces.
- Support showing information of color spaces including CIE 1931 xy chromaticity diagram.
- Support using screen color space defined by operating system first (```Windows``` and ```macOS``` only).
- Support rendering compressed pixel data including ```HEIF```/```JPEG```/```PNG```.
- Add ```Linear sRGB```/```BT.2100 (HLG)```/```BT.2100 (PQ)``` built-in color spaces.
- Add ```ABGR_2101010```/```ARGB_2101010```/```BGRA_1010102```/```RGBA_1010102``` formats.
- Add ```ABGR_F16```/```ARGB_F16```/```BGRA_F16```/```RGBA_F16``` formats.
- Fall back to use embedded ```JPEG``` image if format of raw data in ```DNG``` file is unsupported.
- Support rotating image automatically according to metadata in file.
- Add buttons to adjust filtering parameters accurately.
- Prevent unnecessary image re-rendering.
- Update parameters of ```Display-P3``` color space.
- Update way of expanding color to 16-bit color.
- Update dependent libraries.
- Other bug fixing.

## 📣 What's Change in 2.0
- Support 19 new image formats.
- Support multiple image frames in single source file.
- Support demosaicing for Bayer Pattern format.
- Support color space management.
- Add brightness/contrast/highlight/shadow/saturation/vibrance adjustment.
- Add color balance adjustment.
- Add R/G/B gain adjustment.
- Support showing R/G/B and luminance histograms.
- Support saving image as JPEG or raw BGRA pixels.
- UX improvement.
- Update dependent libraries.
- Bug fixing.


<br/>📔[Back to Home](index.md)
