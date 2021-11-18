using CarinaStudio.Collections;
using Colourful;
using Colourful.Internals;
using System;
using System.Collections.Generic;

namespace Carina.PixelViewer.Media
{
    /// <summary>
    /// RGB color space of bitmap.
    /// </summary>
    abstract unsafe class BitmapColorSpace
    {
        // Static fields.
        static readonly SortedObservableList<BitmapColorSpace> ColorSpaces = new SortedObservableList<BitmapColorSpace>((x, y) =>
            x?.Name?.CompareTo(y?.Name) ?? -1);


        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly BitmapColorSpace BT_2020 = new BT2020BitmapColorSpace();
        /// <summary>
        /// sRGB.
        /// </summary>
        public static readonly BitmapColorSpace Srgb = new SrgbBitmapColorSpace();


        /// <summary>
        /// Default color space which is sRGB.
        /// </summary>
        public static readonly BitmapColorSpace Default = Srgb;


        // BT.2020 color space.
        class BT2020BitmapColorSpace : NonSrgbBitmapColorSpace
        {
            public BT2020BitmapColorSpace() : base("BT.2020", RGBWorkingSpaces.Rec2020) { }
        }


        // Base class for non-sRGB color space.
        abstract class NonSrgbBitmapColorSpace : BitmapColorSpace
        {
            readonly IColorConverter<RGBColor, RGBColor> toSrgbConverter;
            public NonSrgbBitmapColorSpace(string name, IRGBWorkingSpace rgbWorkingSpace) : base(name) 
            {
                this.toSrgbConverter = new ConverterBuilder()
                    .FromRGB(rgbWorkingSpace)
                    .ToRGB(RGBWorkingSpaces.sRGB)
                    .Build();
            }
            public sealed override void ConvertToSrgbColorSpace(double* r, double* g, double* b)
            {
                var color = this.toSrgbConverter.Convert(new RGBColor(*r, *g, *b));
                *r = color.R;
                *g = color.G;
                *b = color.B;
            }
        }


        // sRGB color space.
        class SrgbBitmapColorSpace : BitmapColorSpace
        {
            public SrgbBitmapColorSpace() : base("sRGB") { }
            public override void ConvertToSrgbColorSpace(double* r, double* g, double* b) { }
        }


        /// <summary>
        /// Initialize new <see cref="BitmapColorSpace"/> instance.
        /// </summary>
        /// <param name="name">Name of color space.</param>
        protected BitmapColorSpace(string name)
        {
            this.Name = name;
            ColorSpaces.Add(this);
        }


        /// <summary>
        /// Get all supported color spaces.
        /// </summary>
        public static IList<BitmapColorSpace> All { get; } = ColorSpaces.AsReadOnly();


        /// <summary>
        /// Convert RGB color from current color space to sRGB color space.
        /// </summary>
        /// <param name="r">Pointer to normalized R.</param>
        /// <param name="g">Pointer to normalized G.</param>
        /// <param name="b">Pointer to normalized B.</param>
        public abstract void ConvertToSrgbColorSpace(double* r, double* g, double* b);


        /// <summary>
        /// Get name of color space.
        /// </summary>
        public string Name { get; }


        /// <inheritdoc/>.
        public override string ToString() => this.Name;


        /// <summary>
        /// Try get color space by name.
        /// </summary>
        /// <param name="name">Name of color space.</param>
        /// <param name="colorSpace">Color space with specific name, or <see cref="Default"/> is color space not found.</param>
        /// <returns>True if color space found.</returns>
        public static bool TryGetByName(string? name, out BitmapColorSpace colorSpace)
        {
            if (name != null)
            {
                foreach (var candidate in ColorSpaces)
                {
                    if (candidate.Name == name)
                    {
                        colorSpace = candidate;
                        return true;
                    }
                }
            }
            colorSpace = Default;
            return false;
        }
    }
}
