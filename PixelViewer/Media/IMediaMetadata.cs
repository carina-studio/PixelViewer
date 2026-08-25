using System;

namespace Carina.PixelViewer.Media;

/// <summary>
/// Metadata of media which is parsed from source of media.
/// </summary>
interface IMediaMetadata
{
    /// <summary>
    /// Get manufacturer of camera which captured the media, or Null if it is unknown.
    /// </summary>
    string? CameraManufacturer { get; }


    /// <summary>
    /// Get model of camera which captured the media, or Null if it is unknown.
    /// </summary>
    string? CameraModel { get; }


    /// <summary>
    /// Get the time when the media was created, or Null if it is unknown.
    /// </summary>
    /// <remarks>The offset to UTC is <see cref="TimeSpan.Zero"/> if the source of media doesn't provide the time zone.</remarks>
    DateTimeOffset? CreationTime { get; }


    /// <summary>
    /// Get exposure time to capture the media, or Null if it is unknown.
    /// </summary>
    TimeSpan? ExposureTime { get; }


    /// <summary>
    /// Get F-number of lens to capture the media, or Null if it is unknown.
    /// </summary>
    double? FNumber { get; }


    /// <summary>
    /// Get focal length of lens to capture the media in millimeters, or Null if it is unknown.
    /// </summary>
    double? FocalLength { get; }


    /// <summary>
    /// Get focal length of lens to capture the media in millimeters which is equivalent to 35mm film, or Null if it is unknown.
    /// </summary>
    int? FocalLengthIn35mmFilm { get; }


    /// <summary>
    /// Get ISO speed to capture the media, or Null if it is unknown.
    /// </summary>
    int? IsoSpeed { get; }


    /// <summary>
    /// Get manufacturer of lens which captured the media, or Null if it is unknown.
    /// </summary>
    string? LensManufacturer { get; }


    /// <summary>
    /// Get model of lens which captured the media, or Null if it is unknown.
    /// </summary>
    string? LensModel { get; }


    /// <summary>
    /// Get name of software which generated the media, or Null if it is unknown.
    /// </summary>
    string? Software { get; }
}


/// <summary>
/// Extensions for <see cref="IMediaMetadata"/>.
/// </summary>
static class MediaMetadataExtensions
{
    extension(IMediaMetadata metadata)
    {
        /// <summary>
        /// Find the metadata with the given type from the metadata itself and the metadata which are combined by it.
        /// </summary>
        /// <typeparam name="T">Type of metadata to be found.</typeparam>
        /// <returns>Found metadata, or Null if the metadata cannot be found.</returns>
        public T? Find<T>() where T : class, IMediaMetadata
        {
            // the metadata itself may be the metadata to be found
            if (metadata is T foundMetadata)
                return foundMetadata;

            // find from the metadata which are combined by the metadata
            if (metadata is CompoundMediaMetadata compoundMetadata)
            {
                foreach (var element in compoundMetadata.Elements)
                {
                    var foundElement = element.Find<T>();
                    if (foundElement is not null)
                        return foundElement;
                }
            }
            return null;
        }
    }
}
