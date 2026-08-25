using System;
using System.Collections.Generic;
using System.Linq;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which combines the metadata parsed from the different sources of the same media.
/// </summary>
/// <param name="elements">Metadata to be combined, the metadata which is placed before the others is preferred. The metadata which is Null is dropped.</param>
abstract class CompoundMediaMetadata(params IMediaMetadata?[] elements) : IMediaMetadata
{
    /// <inheritdoc/>
    public virtual string? CameraManufacturer => this.SelectValue(it => it.CameraManufacturer);


    /// <inheritdoc/>
    public virtual string? CameraModel => this.SelectValue(it => it.CameraModel);


    /// <inheritdoc/>
    public virtual DateTimeOffset? CreationTime => this.SelectValue(it => it.CreationTime);


    /// <summary>
    /// Get the metadata which are combined by the metadata, the metadata which is placed before the others is preferred.
    /// </summary>
    public IReadOnlyList<IMediaMetadata> Elements { get; } = elements.OfType<IMediaMetadata>().ToArray();


    /// <inheritdoc/>
    public virtual TimeSpan? ExposureTime => this.SelectValue(it => it.ExposureTime);


    /// <inheritdoc/>
    public virtual double? FNumber => this.SelectValue(it => it.FNumber);


    /// <inheritdoc/>
    public virtual double? FocalLength => this.SelectValue(it => it.FocalLength);


    /// <inheritdoc/>
    public virtual int? FocalLengthIn35mmFilm => this.SelectValue(it => it.FocalLengthIn35mmFilm);


    /// <inheritdoc/>
    public virtual int? IsoSpeed => this.SelectValue(it => it.IsoSpeed);


    /// <inheritdoc/>
    public virtual string? LensManufacturer => this.SelectValue(it => it.LensManufacturer);


    /// <inheritdoc/>
    public virtual string? LensModel => this.SelectValue(it => it.LensModel);


    /// <summary>
    /// Select the value which is provided by the preferred metadata among the combined metadata.
    /// </summary>
    /// <typeparam name="T">Type of value.</typeparam>
    /// <param name="selector">Function to get the value from each of the combined metadata.</param>
    /// <returns>Selected value, or Null if the value is provided by none of the combined metadata.</returns>
    protected T? SelectValue<T>(Func<IMediaMetadata, T?> selector) where T : class
    {
        foreach (var element in this.Elements)
        {
            var value = selector(element);
            if (value is not null)
                return value;
        }
        return null;
    }


    /// <summary>
    /// Select the value which is provided by the preferred metadata among the combined metadata.
    /// </summary>
    /// <typeparam name="T">Type of value.</typeparam>
    /// <param name="selector">Function to get the value from each of the combined metadata.</param>
    /// <returns>Selected value, or Null if the value is provided by none of the combined metadata.</returns>
    protected T? SelectValue<T>(Func<IMediaMetadata, T?> selector) where T : struct
    {
        foreach (var element in this.Elements)
        {
            var value = selector(element);
            if (value.HasValue)
                return value;
        }
        return null;
    }


    /// <inheritdoc/>
    public virtual string? Software => this.SelectValue(it => it.Software);
}
