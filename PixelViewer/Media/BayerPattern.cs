namespace Carina.PixelViewer.Media;

/// <summary>
/// Pattern of Bayer Filter.
/// </summary>
public enum BayerPattern
{
    /// <summary>
    /// 2x2 BGGR.
    /// </summary>
    BGGR_2x2,
    /// <summary>
    /// 2x2 GBRG.
    /// </summary>
    GBRG_2x2,
    /// <summary>
    /// 2x2 GRBG.
    /// </summary>
    GRBG_2x2,
    /// <summary>
    /// 2x2 RGGB.
    /// </summary>
    RGGB_2x2,
    /// <summary>
    /// 4x4 BGGR.
    /// </summary>
    BGGR_4x4,
    /// <summary>
    /// 4x4 GBRG.
    /// </summary>
    GBRG_4x4,
    /// <summary>
    /// 4x4 GRBG.
    /// </summary>
    GRBG_4x4,
    /// <summary>
    /// 4x4 RGGB.
    /// </summary>
    RGGB_4x4,
    /// <summary>
    /// 6x6 X-Trans.
    /// </summary>
    /// <remarks>Only the phase which starts with a 2x2 green block is defined, so the data of an image in any other of the 18 phases of X-Trans has to be shifted to it.</remarks>
    XTrans_6x6,
}
