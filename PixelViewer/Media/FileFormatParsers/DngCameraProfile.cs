using SkiaSharp;
using System;

namespace Carina.PixelViewer.Media.FileFormatParsers;

/// <summary>
/// Color characterization of the camera which captured a DNG file, converted into a <see cref="ColorSpace"/>.
/// </summary>
/// <remarks>The conversion follows the DNG specification: the two calibrations are interpolated for the illuminant
/// of the shot, then the result is expressed as a matrix which converts camera coordinates to CIE XYZ with D50 white point.</remarks>
class DngCameraProfile
{
    // Constants.
    const int MaxWhiteBalanceIterationCount = 30;
    const double WhiteBalanceIterationTolerance = 0.0000001;


    // Inner type of a 3x3 matrix of double values in row-major order.
    readonly struct Matrix3x3
    {
        // Fields.
        readonly double[] values;

        // Constructor.
        public Matrix3x3(double[] values) =>
            this.values = values;

        // Create a diagonal matrix.
        public static Matrix3x3 Diagonal(double m00, double m11, double m22) =>
            new([ m00, 0, 0, 0, m11, 0, 0, 0, m22 ]);

        // Identity matrix.
        public static readonly Matrix3x3 Identity = new([ 1, 0, 0, 0, 1, 0, 0, 0, 1 ]);

        // Get the inverse of the matrix, or null if the matrix is singular.
        public Matrix3x3? Invert()
        {
            var m = this.values;
            var c00 = m[4] * m[8] - m[5] * m[7];
            var c01 = m[5] * m[6] - m[3] * m[8];
            var c02 = m[3] * m[7] - m[4] * m[6];
            var determinant = m[0] * c00 + m[1] * c01 + m[2] * c02;
            if (Math.Abs(determinant) < 1e-12 || !double.IsFinite(determinant))
                return null;
            var scale = 1 / determinant;
            return new Matrix3x3(
            [
                c00 * scale, (m[2] * m[7] - m[1] * m[8]) * scale, (m[1] * m[5] - m[2] * m[4]) * scale,
                c01 * scale, (m[0] * m[8] - m[2] * m[6]) * scale, (m[2] * m[3] - m[0] * m[5]) * scale,
                c02 * scale, (m[1] * m[6] - m[0] * m[7]) * scale, (m[0] * m[4] - m[1] * m[3]) * scale,
            ]);
        }

        // Interpolate linearly between two matrices.
        public static Matrix3x3 Lerp(Matrix3x3 x, Matrix3x3 y, double weightOfX)
        {
            var weightOfY = 1 - weightOfX;
            var result = new double[9];
            for (var i = 8; i >= 0; --i)
                result[i] = x.values[i] * weightOfX + y.values[i] * weightOfY;
            return new Matrix3x3(result);
        }

        // Multiply two matrices.
        public static Matrix3x3 operator *(Matrix3x3 x, Matrix3x3 y)
        {
            var result = new double[9];
            for (var row = 0; row < 3; ++row)
            {
                for (var column = 0; column < 3; ++column)
                {
                    var offset = row * 3;
                    result[offset + column] = x.values[offset] * y.values[column]
                        + x.values[offset + 1] * y.values[column + 3]
                        + x.values[offset + 2] * y.values[column + 6];
                }
            }
            return new Matrix3x3(result);
        }

        // Multiply the matrix by a vector.
        public static (double, double, double) operator *(Matrix3x3 x, (double, double, double) v) =>
            (x.values[0] * v.Item1 + x.values[1] * v.Item2 + x.values[2] * v.Item3,
             x.values[3] * v.Item1 + x.values[4] * v.Item2 + x.values[5] * v.Item3,
             x.values[6] * v.Item1 + x.values[7] * v.Item2 + x.values[8] * v.Item3);

        // Convert to the Skia matrix consumed by ColorSpace.
        public SKColorSpaceXyz ToSKColorSpaceXyz()
        {
            var result = new float[9];
            for (var i = 8; i >= 0; --i)
                result[i] = (float)this.values[i];
            return new SKColorSpaceXyz(result);
        }

        // Check whether every element of the matrix is a finite value or not.
        public bool IsFinite
        {
            get
            {
                for (var i = 8; i >= 0; --i)
                {
                    if (!double.IsFinite(this.values[i]))
                        return false;
                }
                return true;
            }
        }
    }


    // Static fields.
    static readonly Matrix3x3 BradfordMatrix = new(
    [
         0.8951,  0.2664, -0.1614,
        -0.7502,  1.7135,  0.0367,
         0.0389, -0.0685,  1.0296,
    ]);
    static readonly (double, double, double) D50Xyz = (0.9642, 1.0000, 0.8249);


    // Fields.
    readonly (double, double, double) asShotNeutral;
    readonly Matrix3x3 cameraCalibration1;
    readonly Matrix3x3 cameraCalibration2;
    readonly Matrix3x3 colorMatrix1;
    readonly Matrix3x3? colorMatrix2;
    readonly Matrix3x3? forwardMatrix1;
    readonly Matrix3x3? forwardMatrix2;
    readonly double illuminantCct1;
    readonly double illuminantCct2;


    // Constructor.
    DngCameraProfile((double, double, double) asShotNeutral, Matrix3x3 analogBalance, Matrix3x3 colorMatrix1, Matrix3x3? colorMatrix2, Matrix3x3? forwardMatrix1, Matrix3x3? forwardMatrix2, Matrix3x3 cameraCalibration1, Matrix3x3 cameraCalibration2, double illuminantCct1, double illuminantCct2)
    {
        // the analog balance is always applied together with the camera calibration, so fold it in once here
        this.asShotNeutral = asShotNeutral;
        this.cameraCalibration1 = analogBalance * cameraCalibration1;
        this.cameraCalibration2 = analogBalance * cameraCalibration2;
        this.colorMatrix1 = colorMatrix1;
        this.colorMatrix2 = colorMatrix2;
        this.forwardMatrix1 = forwardMatrix1;
        this.forwardMatrix2 = forwardMatrix2;
        this.illuminantCct1 = illuminantCct1;
        this.illuminantCct2 = illuminantCct2;
    }


    // Get the matrix which chromatically adapts the given white point to D50 by the Bradford transform.
    static Matrix3x3? CreateBradfordAdaptationMatrix((double, double, double) whitePoint)
    {
        // convert both white points into the cone response domain
        var source = BradfordMatrix * whitePoint;
        var target = BradfordMatrix * D50Xyz;
        if (Math.Abs(source.Item1) < 1e-12 || Math.Abs(source.Item2) < 1e-12 || Math.Abs(source.Item3) < 1e-12)
            return null;

        // scale each cone response independently, then return to XYZ
        var inverseBradfordMatrix = BradfordMatrix.Invert();
        if (!inverseBradfordMatrix.HasValue)
            return null;
        var scale = Matrix3x3.Diagonal(target.Item1 / source.Item1, target.Item2 / source.Item2, target.Item3 / source.Item3);
        return inverseBradfordMatrix.Value * scale * BradfordMatrix;
    }


    /// <summary>
    /// Create the <see cref="ColorSpace"/> whose RGB coordinates are the camera coordinates after the given RGB gains have been applied.
    /// </summary>
    /// <param name="customName">Custom name of the created color space.</param>
    /// <param name="redGain">Gain of red color which has been applied to the camera coordinates.</param>
    /// <param name="greenGain">Gain of green color which has been applied to the camera coordinates.</param>
    /// <param name="blueGain">Gain of blue color which has been applied to the camera coordinates.</param>
    /// <returns><see cref="ColorSpace"/>, or Null if the color space cannot be created.</returns>
    public ColorSpace? CreateColorSpace(string? customName, double redGain, double greenGain, double blueGain)
    {
        // resolve the transform from the raw camera coordinates to CIE XYZ with D50 white point
        var cameraToXyzD50 = this.CreateCameraToXyzD50Matrix();
        if (!cameraToXyzD50.HasValue)
            return null;

        // the renderer has already applied the gains, so the color space takes the gained coordinates as its input
        if (Math.Abs(redGain) < 1e-12 || Math.Abs(greenGain) < 1e-12 || Math.Abs(blueGain) < 1e-12)
            return null;
        var matrix = cameraToXyzD50.Value * Matrix3x3.Diagonal(1 / redGain, 1 / greenGain, 1 / blueGain);
        if (!matrix.IsFinite)
            return null;

        // the camera coordinates are linear
        return ColorSpace.FromMatrixToXyz(ColorSpaceSource.Embedded, customName, matrix.ToSKColorSpaceXyz(), SKColorSpaceTransferFn.Linear, D50Xyz);
    }


    // Create the matrix which converts the raw camera coordinates to CIE XYZ with D50 white point.
    Matrix3x3? CreateCameraToXyzD50Matrix()
    {
        // interpolate the calibration for the illuminant of the shot
        var weightOf1 = this.SolveIlluminantWeight();
        var colorMatrix = this.colorMatrix2.HasValue
            ? Matrix3x3.Lerp(this.colorMatrix1, this.colorMatrix2.Value, weightOf1)
            : this.colorMatrix1;
        var cameraCalibration = this.colorMatrix2.HasValue
            ? Matrix3x3.Lerp(this.cameraCalibration1, this.cameraCalibration2, weightOf1)
            : this.cameraCalibration1;
        var inverseCameraCalibration = cameraCalibration.Invert();
        if (!inverseCameraCalibration.HasValue)
            return null;

        // the forward matrix maps the white balanced camera coordinates directly to the D50 connection space
        if (this.forwardMatrix1.HasValue)
        {
            var forwardMatrix = (this.forwardMatrix2.HasValue && this.colorMatrix2.HasValue)
                ? Matrix3x3.Lerp(this.forwardMatrix1.Value, this.forwardMatrix2.Value, weightOf1)
                : this.forwardMatrix1.Value;

            // the specification requires the forward matrix to map the reference neutral exactly onto D50, but the
            // values stored in the file are quantized, so adapt away the residual error before using it
            var normalizationMatrix = CreateBradfordAdaptationMatrix(forwardMatrix * (1.0, 1.0, 1.0));
            if (normalizationMatrix.HasValue)
                forwardMatrix = normalizationMatrix.Value * forwardMatrix;

            var referenceNeutral = inverseCameraCalibration.Value * this.asShotNeutral;
            if (Math.Abs(referenceNeutral.Item1) < 1e-12 || Math.Abs(referenceNeutral.Item2) < 1e-12 || Math.Abs(referenceNeutral.Item3) < 1e-12)
                return null;
            var neutralToReference = Matrix3x3.Diagonal(1 / referenceNeutral.Item1, 1 / referenceNeutral.Item2, 1 / referenceNeutral.Item3);
            return forwardMatrix * neutralToReference * inverseCameraCalibration.Value;
        }

        // without a forward matrix the color matrix is inverted, then the white of the shot is adapted to D50
        var cameraToXyz = (cameraCalibration * colorMatrix).Invert();
        if (!cameraToXyz.HasValue)
            return null;
        var whitePoint = cameraToXyz.Value * this.asShotNeutral;
        var adaptationMatrix = CreateBradfordAdaptationMatrix(whitePoint);
        if (!adaptationMatrix.HasValue)
            return null;
        return adaptationMatrix.Value * cameraToXyz.Value;
    }


    // Get the correlated color temperature of the given calibration illuminant, or NaN if it is unknown.
    static double IlluminantToCct(int illuminant) => illuminant switch
    {
        1 or 4 or 9 => 5503, // daylight, flash, fine weather
        2 => 4150, // fluorescent
        3 or 17 => 2856, // tungsten, standard light A
        10 => 6504, // cloudy
        11 => 7504, // shade
        12 => 6430, // daylight fluorescent
        13 => 5000, // day white fluorescent
        14 => 4150, // cool white fluorescent
        15 => 3450, // white fluorescent
        18 => 4874, // standard light B
        19 => 6774, // standard light C
        20 => 5503, // D55
        21 => 6504, // D65
        22 => 7504, // D75
        23 => 5003, // D50
        24 => 3200, // ISO studio tungsten
        _ => double.NaN,
    };


    // Solve the weight of the first calibration for the illuminant of the shot.
    double SolveIlluminantWeight()
    {
        // a single calibration, or calibrations which cannot be told apart, need no interpolation
        if (!this.colorMatrix2.HasValue
            || !double.IsFinite(this.illuminantCct1)
            || !double.IsFinite(this.illuminantCct2)
            || Math.Abs(this.illuminantCct1 - this.illuminantCct2) < 1)
        {
            return 1;
        }

        // the illuminant of the shot is only known in camera coordinates, so converge on it: interpolate the color
        // matrix for the current estimate, map the neutral through it, and take the temperature of the result
        var weight = 0.5;
        var x = 0.0;
        var y = 0.0;
        for (var i = 0; i < MaxWhiteBalanceIterationCount; ++i)
        {
            var colorMatrix = Matrix3x3.Lerp(this.colorMatrix1, this.colorMatrix2.Value, weight);
            var inverseColorMatrix = colorMatrix.Invert();
            if (!inverseColorMatrix.HasValue)
                return weight;
            var xyzOfNeutral = inverseColorMatrix.Value * this.asShotNeutral;
            var (newX, newY) = ColorSpace.XyzToXyChromaticity(xyzOfNeutral);
            if (!double.IsFinite(newX) || !double.IsFinite(newY))
                return weight;
            var isConverged = Math.Abs(newX - x) < WhiteBalanceIterationTolerance && Math.Abs(newY - y) < WhiteBalanceIterationTolerance;
            x = newX;
            y = newY;
            weight = this.WeightOfCct(ColorSpace.XyChromaticityToCct(newX, newY));
            if (isConverged)
                break;
        }
        return weight;
    }


    /// <summary>
    /// Try creating <see cref="DngCameraProfile"/> from the values of the color characterization tags of a DNG file.
    /// </summary>
    /// <param name="asShotNeutral">Value of AsShotNeutral tag.</param>
    /// <param name="analogBalance">Value of AnalogBalance tag, or Null if it is absent.</param>
    /// <param name="colorMatrix1">Value of ColorMatrix1 tag.</param>
    /// <param name="colorMatrix2">Value of ColorMatrix2 tag, or Null if it is absent.</param>
    /// <param name="forwardMatrix1">Value of ForwardMatrix1 tag, or Null if it is absent.</param>
    /// <param name="forwardMatrix2">Value of ForwardMatrix2 tag, or Null if it is absent.</param>
    /// <param name="cameraCalibration1">Value of CameraCalibration1 tag, or Null if it is absent.</param>
    /// <param name="cameraCalibration2">Value of CameraCalibration2 tag, or Null if it is absent.</param>
    /// <param name="calibrationIlluminant1">Value of CalibrationIlluminant1 tag.</param>
    /// <param name="calibrationIlluminant2">Value of CalibrationIlluminant2 tag.</param>
    /// <param name="profile">Created <see cref="DngCameraProfile"/>.</param>
    /// <returns>True if the profile was created successfully.</returns>
    public static bool TryCreate(double[]? asShotNeutral, double[]? analogBalance, double[]? colorMatrix1, double[]? colorMatrix2, double[]? forwardMatrix1, double[]? forwardMatrix2, double[]? cameraCalibration1, double[]? cameraCalibration2, int calibrationIlluminant1, int calibrationIlluminant2, out DngCameraProfile? profile)
    {
        // the as-shot neutral and at least one color matrix are the minimum needed to characterize the camera
        profile = null;
        if (!TryConvertToVector(asShotNeutral, out var neutral)
            || !TryConvertToMatrix(colorMatrix1, out var cm1))
        {
            return false;
        }

        // every other tag is optional and defaults to the identity, and a second calibration is only usable as a pair
        TryConvertToMatrix(colorMatrix2, out var cm2);
        TryConvertToMatrix(forwardMatrix1, out var fm1);
        TryConvertToMatrix(forwardMatrix2, out var fm2);
        if (!TryConvertToMatrix(cameraCalibration1, out var cc1))
            cc1 = Matrix3x3.Identity;
        if (!TryConvertToMatrix(cameraCalibration2, out var cc2))
            cc2 = Matrix3x3.Identity;
        var ab = TryConvertToVector(analogBalance, out var abVector)
            ? Matrix3x3.Diagonal(abVector.Item1, abVector.Item2, abVector.Item3)
            : Matrix3x3.Identity;

        // complete
        profile = new DngCameraProfile(neutral, ab, cm1!.Value, cm2, fm1, fm2, cc1!.Value, cc2!.Value, IlluminantToCct(calibrationIlluminant1), IlluminantToCct(calibrationIlluminant2));
        return true;
    }


    // Try converting the value of a tag into a 3x3 matrix.
    static bool TryConvertToMatrix(double[]? values, out Matrix3x3? matrix)
    {
        matrix = null;
        if (values is null || values.Length < 9)
            return false;
        for (var i = 8; i >= 0; --i)
        {
            if (!double.IsFinite(values[i]))
                return false;
        }
        matrix = new Matrix3x3([ values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8] ]);
        return true;
    }


    // Try converting the value of a tag into a vector with 3 elements.
    static bool TryConvertToVector(double[]? values, out (double, double, double) vector)
    {
        vector = default;
        if (values is null || values.Length < 3)
            return false;
        for (var i = 2; i >= 0; --i)
        {
            if (!double.IsFinite(values[i]) || Math.Abs(values[i]) < 1e-12)
                return false;
        }
        vector = (values[0], values[1], values[2]);
        return true;
    }


    // Get the weight of the first calibration for the given correlated color temperature, interpolated in mired.
    double WeightOfCct(double cct)
    {
        if (!double.IsFinite(cct) || cct < 1)
            return 1;
        var mired = 1000000 / cct;
        var mired1 = 1000000 / this.illuminantCct1;
        var mired2 = 1000000 / this.illuminantCct2;
        var weight = (mired - mired2) / (mired1 - mired2);
        if (weight <= 0)
            return 0;
        if (weight >= 1)
            return 1;
        return weight;
    }
}
