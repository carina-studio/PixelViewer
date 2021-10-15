using CarinaStudio;
using CarinaStudio.Collections;
using CarinaStudio.Threading;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Carina.PixelViewer.Media.Profiles
{
    /// <summary>
    /// Control and manage <see cref="ImageRenderingProfile"/>.
    /// </summary>
    static class ImageRenderingProfiles
    {
        // Fields.
        static volatile IApplication? app;
        static volatile ILogger? logger;
        static readonly SortedObservableList<ImageRenderingProfile> profiles = new SortedObservableList<ImageRenderingProfile>((x, y) =>
        {
            if (x == null || x.IsDefault)
                return -1;
            if (y == null || y.IsDefault)
                return 1;
            var result = x.Name.CompareTo(y.Name);
            return result != 0 ? result : x.GetHashCode() - y.GetHashCode();
        });


        // Initializer.
        static ImageRenderingProfiles()
        {
            Profiles = profiles.AsReadOnly();
        }


        // Add new profile.
        public static bool AddProfile(ImageRenderingProfile profile)
        {
            // check state
            app.AsNonNull().VerifyAccess();
            if (!ValidateNewProfileName(profile.Name))
                return false;

            // start saving to file
            _ = profile.SaveAsync();

            // add to list
            profile.PropertyChanged += OnProfilePropertyChanged;
            profiles.Add(profile);
            return true;
        }


        // Initialize.
        public static async Task InitializeAsync(IApplication app)
        {
            // check state
            lock (typeof(ImageRenderingProfiles))
            {
                if (ImageRenderingProfiles.app != null)
                    throw new InvalidOperationException("Unexpected initialization.");
                ImageRenderingProfiles.app = app;
            }

            // create logger
            logger = app.LoggerFactory.CreateLogger(nameof(ImageRenderingProfiles));
            logger.LogDebug("Initialize");

            // initialize profile
            ImageRenderingProfile.Initialize(app);
            profiles.Add(ImageRenderingProfile.Default);

            // find profile files
            var fileNames = await Task.Run(() =>
            {
                var fileNames = new List<string>();
                try
                {
                    if (Directory.Exists(ImageRenderingProfile.DirectoryPath))
                        fileNames.AddRange(Directory.EnumerateFiles(ImageRenderingProfile.DirectoryPath));
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error occurred while getting file names of profile in '{ImageRenderingProfile.DirectoryPath}'");
                }
                return fileNames;
            });
            logger.LogTrace($"{fileNames.Count} profile file(s) found");

            // load profiles
            foreach (var fileName in fileNames)
            {
                var profile = (ImageRenderingProfile?)null;
                try
                {
                    logger.LogTrace($"Load '{fileName}'");
                    profile = await ImageRenderingProfile.LoadAsync(fileName);
                }
                catch(Exception ex)
                {
                    logger.LogError(ex, $"Unable to load '{fileName}'");
                    continue;
                }
                if (!ValidateNewProfileName(profile.Name))
                {
                    logger.LogError($"Duplicate name of profile '{profile.Name}'");
                    continue;
                }
                if (profile.IsUpgradedWhenLoading)
                {
                    logger.LogWarning($"Profile '{profile.Name}' was upgraded, save back to file");
                    _ = profile.SaveAsync();
                }
                profile.PropertyChanged += OnProfilePropertyChanged;
                profiles.Add(profile);
            }
            logger.LogDebug($"{profiles.Count - 1} profile(s) loaded");
        }


        // Property of profile changed.
        static void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ImageRenderingProfile profile)
                return;
            if (e.PropertyName == nameof(ImageRenderingProfile.Name))
            {
                var newName = profile.Name;
                profiles.Sort(profile);
                if (profiles.FirstOrDefault(it => !it.IsDefault && it != profile && it.Name == newName) != null)
                {
                    logger?.LogError($"Duplicate profile name '{newName}', remove changed profile");
                    RemoveProfile(profile);
                }
            }
        }


        // Get all profiles.
        public static IList<ImageRenderingProfile> Profiles { get; }


        // Remove profile.
        public static void RemoveProfile(ImageRenderingProfile profile)
        {
            app.AsNonNull().VerifyAccess();
            if (profiles.Remove(profile))
            {
                profile.PropertyChanged -= OnProfilePropertyChanged;
                _ = profile.DeleteFileAsync();
            }
        }


        // Check whether given name of profile is valid or not.
        public static bool ValidateNewProfileName(string name) => profiles.FirstOrDefault(it => !it.IsDefault && it.Name == name) == null;


        // Wait for IO tasks complete.
        public static Task WaitForIOTasksAsync() => ImageRenderingProfile.IOTaskFactory.StartNew(() => logger?.LogDebug("All I/O tasks completed"));
    }
}
