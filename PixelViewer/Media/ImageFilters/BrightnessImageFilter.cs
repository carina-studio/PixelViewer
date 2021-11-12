using System;

namespace Carina.PixelViewer.Media.ImageFilters
{
    /// <summary>
    /// Brightness adjustment filter.
    /// </summary>
    class BrightnessImageFilter : BaseColorTransformImageFilter<BrightnessImageFilter.Params>
    {
        /// <summary>
        /// Parameters.
        /// </summary>
        public class Params : ImageFilterParams
        {
            /// <inheritdoc/>
            public override object Clone() => new Params()
            {
                Factor = this.Factor
            };


            /// <summary>
            /// Get or set factor of brightness adjustment.
            /// </summary>
            public double Factor { get; set; } = 1;
        }


        /// <inheritdoc/>
        protected override void ParseColorTransforms(Params parameters, out double rFactor, out double gFactor, out double bFactor, out double aFactor)
        {
            if (!double.IsFinite(parameters.Factor) || parameters.Factor < 0)
                throw new ArgumentOutOfRangeException();
            rFactor = parameters.Factor;
            gFactor = parameters.Factor;
            bFactor = parameters.Factor;
            aFactor = 1;
        }
    }
}
