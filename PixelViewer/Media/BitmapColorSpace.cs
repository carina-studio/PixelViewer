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
        static readonly XYZColor D63 = new ConverterBuilder().Fromxy().ToXYZ().Build().Convert(new xyChromaticity(0.314, 0.351));
        static readonly IRGBWorkingSpace DciP3WorkingSpace = new RGBWorkingSpace(D63,
                new sRGBCompanding(), // refer to https://en.wikipedia.org/wiki/DCI-P3
                new RGBPrimaries(
                    new xyChromaticity(0.68, 0.32),
                    new xyChromaticity(0.265, 0.69),
                    new xyChromaticity(0.15, 0.06)
                )
            );
        static readonly IRGBWorkingSpace DisplayP3WorkingSpace = new RGBWorkingSpace(Illuminants.D65,
                new sRGBCompanding(),
                new RGBPrimaries(
                    new xyChromaticity(0.68, 0.32),
                    new xyChromaticity(0.265, 0.69),
                    new xyChromaticity(0.15, 0.06)
                )
            );


        /// <summary>
        /// Adobe RGB.
        /// </summary>
        public static readonly BitmapColorSpace AdobeRgb = new AdobeRgbBitmapColorSpace();
        /// <summary>
        /// ITU-R BT.2020.
        /// </summary>
        public static readonly BitmapColorSpace BT_2020 = new BT2020BitmapColorSpace();
        /// <summary>
        /// ITU-R BT.601.
        /// </summary>
        public static readonly BitmapColorSpace BT_601 = new BT601BitmapColorSpace();
        /// <summary>
        /// DCI-P3.
        /// </summary>
        public static readonly BitmapColorSpace DCI_P3 = new DciP3BitmapColorSpace();
        /// <summary>
        /// Display-P3.
        /// </summary>
        public static readonly BitmapColorSpace Display_P3 = new DisplayP3BitmapColorSpace();
        /// <summary>
        /// sRGB.
        /// </summary>
        public static readonly BitmapColorSpace Srgb = new SrgbBitmapColorSpace();


        /// <summary>
        /// Default color space which is sRGB.
        /// </summary>
        public static readonly BitmapColorSpace Default = Srgb;


        // Adobe RGB color space.
        class AdobeRgbBitmapColorSpace : BitmapColorSpace
        {
            public AdobeRgbBitmapColorSpace() : base("Adobe-RGB", RGBWorkingSpaces.AdobeRGB1998) { }
        }


        // BT.2020 color space.
        class BT2020BitmapColorSpace : BitmapColorSpace
        {
            public BT2020BitmapColorSpace() : base("BT.2020", RGBWorkingSpaces.Rec2020) { }
        }


        // BT.601 color space.
        class BT601BitmapColorSpace : BitmapColorSpace
        {
            class BT601Companding : ICompanding
            {
                public double ConvertToLinear(in double nonLinearChannel) => nonLinearChannel < 0.081
                    ? nonLinearChannel / 4.5
                    : Math.Pow(Math.E, Math.Log((nonLinearChannel + 0.099) / 1.099) / 0.45);
                public double ConvertToNonLinear(in double linearChannel) => linearChannel < 0.018
                    ? 4.5 * linearChannel
                    : 1.099 * Math.Pow(linearChannel, 0.45) - 0.099;
            }
            static readonly IRGBWorkingSpace RGBWorkingSpace = new RGBWorkingSpace(Illuminants.D65, 
                new BT601Companding(), 
                new RGBPrimaries(
                    new xyChromaticity(0.64, 0.33),
                    new xyChromaticity(0.29, 0.60),
                    new xyChromaticity(0.15, 0.06)
                )
            );
            public BT601BitmapColorSpace() : base("BT.601", RGBWorkingSpace) { }
        }


        // DCI-P3 color space.
        class DciP3BitmapColorSpace : BitmapColorSpace
        {
            public DciP3BitmapColorSpace() : base("DCI-P3", DciP3WorkingSpace, 2.6) { }
            public override unsafe void ConvertToDciP3ColorSpace(double* r, double* g, double* b) { }
        }


        // Display-P3 color space.
        class DisplayP3BitmapColorSpace : BitmapColorSpace
        {
            public DisplayP3BitmapColorSpace() : base("Display-P3", DisplayP3WorkingSpace) { }
            public override unsafe void ConvertToDisplayP3ColorSpace(double* r, double* g, double* b) { }
        }


        // sRGB color space.
        class SrgbBitmapColorSpace : BitmapColorSpace
        {
            public SrgbBitmapColorSpace() : base("sRGB", RGBWorkingSpaces.sRGB) { }
            public override void ConvertToSrgbColorSpace(double* r, double* g, double* b) { }
        }


        // Fields.
        readonly double coeffToGamma22;
        readonly double coeffToGamma26;
        readonly double gamma;
        readonly bool isGamma22;
        readonly bool isGamma26;
        readonly IColorConverter<RGBColor, RGBColor> toDciP3Converter;
        readonly IColorConverter<RGBColor, RGBColor> toDisplayP3Converter;
        readonly IColorConverter<RGBColor, RGBColor> toSrgbConverter;


       // Constructor.
        private BitmapColorSpace(string name, IRGBWorkingSpace rgbWorkingSpace, double gamma = 2.2)
        {
            this.Name = name;
            ColorSpaces.Add(this);
            this.coeffToGamma22 = gamma / 2.2;
            this.coeffToGamma26 = gamma / 2.6;
            this.gamma = gamma;
            this.isGamma22 = Math.Abs(gamma - 2.2) <= 0.001;
            this.isGamma26 = !this.isGamma22 && Math.Abs(gamma - 2.6) <= 0.001;
            this.toDciP3Converter = new ConverterBuilder()
                    .FromRGB(rgbWorkingSpace)
                    .ToRGB(DciP3WorkingSpace)
                    .Build();
            this.toDisplayP3Converter = new ConverterBuilder()
                    .FromRGB(rgbWorkingSpace)
                    .ToRGB(DisplayP3WorkingSpace)
                    .Build();
            this.toSrgbConverter = new ConverterBuilder()
                    .FromRGB(rgbWorkingSpace)
                    .ToRGB(RGBWorkingSpaces.sRGB)
                    .Build();
        }


        /// <summary>
        /// Get all supported color spaces.
        /// </summary>
        public static IList<BitmapColorSpace> All { get; } = ColorSpaces.AsReadOnly();


        /// <summary>
        /// Convert RGB color from current color space to DCI-P3 color space.
        /// </summary>
        /// <param name="r">Pointer to normalized R.</param>
        /// <param name="g">Pointer to normalized G.</param>
        /// <param name="b">Pointer to normalized B.</param>
        public virtual void ConvertToDciP3ColorSpace(double* r, double* g, double* b)
        {
            var color = this.toDciP3Converter.Convert(new RGBColor(*r, *g, *b));
            if (this.isGamma26)
            {
                *r = color.R;
                *g = color.G;
                *b = color.B;
            }
            else
            {
                var coeff = this.coeffToGamma26;
                *r = Math.Pow(Math.E, coeff * Math.Log(color.R));
                *g = Math.Pow(Math.E, coeff * Math.Log(color.G));
                *b = Math.Pow(Math.E, coeff * Math.Log(color.B));
            }
        }


        /// <summary>
        /// Convert RGB color from current color space to Display-P3 color space.
        /// </summary>
        /// <param name="r">Pointer to normalized R.</param>
        /// <param name="g">Pointer to normalized G.</param>
        /// <param name="b">Pointer to normalized B.</param>
        public virtual void ConvertToDisplayP3ColorSpace(double* r, double* g, double* b)
        {
            var color = this.toDisplayP3Converter.Convert(new RGBColor(*r, *g, *b));
            if (this.isGamma22)
            {
                *r = color.R;
                *g = color.G;
                *b = color.B;
            }
            else
            {
                var coeff = this.coeffToGamma22;
                *r = Math.Pow(Math.E, coeff * Math.Log(color.R));
                *g = Math.Pow(Math.E, coeff * Math.Log(color.G));
                *b = Math.Pow(Math.E, coeff * Math.Log(color.B));
            }
        }


        /// <summary>
        /// Convert RGB color from current color space to sRGB color space.
        /// </summary>
        /// <param name="r">Pointer to normalized R.</param>
        /// <param name="g">Pointer to normalized G.</param>
        /// <param name="b">Pointer to normalized B.</param>
        public virtual void ConvertToSrgbColorSpace(double* r, double* g, double* b)
        {
            var color = this.toSrgbConverter.Convert(new RGBColor(*r, *g, *b));
            if (this.isGamma22)
            {
                *r = color.R;
                *g = color.G;
                *b = color.B;
            }
            else
            {
                var coeff = this.coeffToGamma22;
                *r = Math.Pow(Math.E, coeff * Math.Log(color.R));
                *g = Math.Pow(Math.E, coeff * Math.Log(color.G));
                *b = Math.Pow(Math.E, coeff * Math.Log(color.B));
            }
        }


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
