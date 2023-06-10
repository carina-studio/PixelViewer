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
        static readonly ObservableList<ImageRenderingProfile> userDefinedProfiles = new ObservableList<ImageRenderingProfile>();


        // Initializer.
        static ImageRenderingProfiles()
        {
            UserDefinedProfiles = ListExtensions.AsReadOnly(userDefinedProfiles);
        }


        // Add new profile.
        public static bool AddUserDefinedProfile(ImageRenderingProfile profile)
        {
            // check state
            app.AsNonNull().VerifyAccess();
            if (profile.Type != ImageRenderingProfileType.UserDefined)
                return false;
            if (!ValidateNewUserDefinedProfileName(profile.Name))
                return false;

            // start saving to file
            _ = profile.SaveAsync();

            // add to list
            profile.PropertyChanged += OnProfilePropertyChanged;
            userDefinedProfiles.Add(profile);
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
                    logger.LogError(ex, "Error occurred while getting file names of profile in '{path}'", ImageRenderingProfile.DirectoryPath);
                }
                return fileNames;
            });
            logger.LogTrace("{count} user-defined profile file(s) found", fileNames.Count);

            // load user defined profiles
            foreach (var fileName in fileNames)
            {
                ImageRenderingProfile? profile;
                try
                {
                    logger.LogTrace("Load '{fileName}'", fileName);
                    profile = await ImageRenderingProfile.LoadAsync(fileName);
                }
                catch(Exception ex)
                {
                    logger.LogError(ex, "Unable to load '{fileName}'", fileName);
                    continue;
                }
                if (!ValidateNewUserDefinedProfileName(profile.Name))
                {
                    logger.LogError("Duplicate name of user-defined profile '{name}'", profile.Name);
                    continue;
                }
                if (profile.IsUpgradedWhenLoading)
                {
                    logger.LogWarning("User-defined profile '{name}' was upgraded, save back to file", profile.Name);
                    _ = profile.SaveAsync();
                }
                profile.PropertyChanged += OnProfilePropertyChanged;
                userDefinedProfiles.Add(profile);
            }
            logger.LogDebug("{count} user-defined profile(s) loaded", userDefinedProfiles.Count);
        }


        // Property of profile changed.
        static void OnProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ImageRenderingProfile profile)
                return;
            if (e.PropertyName == nameof(ImageRenderingProfile.Name))
            {
                var newName = profile.Name;
                if (userDefinedProfiles.FirstOrDefault(it => it.Type == ImageRenderingProfileType.UserDefined && it != profile && it.Name == newName) != null)
                {
                    logger?.LogError("Duplicate profile name '{newName}', remove changed profile", newName);
                    RemoveUserDefinedProfile(profile);
                }
            }
        }


        // Get all user defined profiles.
        public static IList<ImageRenderingProfile> UserDefinedProfiles { get; }


        // Remove user defined profile.
        public static void RemoveUserDefinedProfile(ImageRenderingProfile profile)
        {
            app.AsNonNull().VerifyAccess();
            var index = userDefinedProfiles.IndexOf(profile);
            if (index >= 0)
            {
                userDefinedProfiles.RemoveAt(index);
                profile.PropertyChanged -= OnProfilePropertyChanged;
                _ = profile.DeleteFileAsync();
            }
        }


        // Check whether given name of profile is valid or not.
        public static bool ValidateNewUserDefinedProfileName(string name) => userDefinedProfiles.FirstOrDefault(it => it.Type == ImageRenderingProfileType.UserDefined && it.Name == name) == null;


        // Wait for IO tasks complete.
        public static Task WaitForIOTasksAsync() => ImageRenderingProfile.IOTaskFactory.StartNew(() => logger?.LogDebug("All I/O tasks completed"));
    }
}
