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

namespace Carina.PixelViewer.Media.Profiles;

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


    // Initialize. Calling the method again by the same application is allowed and does nothing.
    public static async Task InitializeAsync(IApplication app)
    {
        // check state
        lock (typeof(ImageRenderingProfiles))
        {
            if (ImageRenderingProfiles.app is not null)
            {
                if (ImageRenderingProfiles.app != app)
                    throw new InvalidOperationException("Profiles have been initialized by another application.");
                return;
            }
            ImageRenderingProfiles.app = app;
        }

        // create logger
        logger = app.LoggerFactory.CreateLogger(nameof(ImageRenderingProfiles));
        logger.LogDebug("Initialize");

        // initialize profile
        ImageRenderingProfile.Initialize(app);

        // load user-defined profiles
        await LoadUserDefinedProfilesAsync();
    }


    // Load user-defined profiles from files which are not in the list yet.
    public static async Task LoadUserDefinedProfilesAsync()
    {
        // check state
        app.AsNonNull().VerifyAccess();

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
                logger?.LogError(ex, "Error occurred while getting file names of profile in '{path}'", ImageRenderingProfile.DirectoryPath);
            }
            return fileNames;
        });

        // load profiles which are not in the list yet
        var count = 0;
        foreach (var fileName in fileNames)
        {
            // load profile, its renderer may still be unavailable
            ImageRenderingProfile profile;
            try
            {
                profile = await ImageRenderingProfile.LoadAsync(fileName);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Unable to load '{fileName}', its renderer may be unavailable", fileName);
                continue;
            }

            // skip the profile which is already in the list
            if (!ValidateNewUserDefinedProfileName(profile.Name))
            {
                profile.Dispose();
                continue;
            }

            // add to list
            if (profile.IsUpgradedWhenLoading)
            {
                logger?.LogWarning("User-defined profile '{name}' was upgraded, save back to file", profile.Name);
                _ = profile.SaveAsync();
            }
            profile.PropertyChanged += OnProfilePropertyChanged;
            userDefinedProfiles.Add(profile);
            ++count;
        }
        logger?.LogDebug("{count} user-defined profile(s) loaded", count);
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


    // Remove user defined profile. The file of the profile is kept when deleteFile is false, so that the profile can be loaded again later.
    public static void RemoveUserDefinedProfile(ImageRenderingProfile profile, bool deleteFile = true)
    {
        app.AsNonNull().VerifyAccess();
        var index = userDefinedProfiles.IndexOf(profile);
        if (index >= 0)
        {
            userDefinedProfiles.RemoveAt(index);
            profile.PropertyChanged -= OnProfilePropertyChanged;
            if (deleteFile)
                _ = profile.DeleteFileAsync();
        }
    }


    // Check whether given name of profile is valid or not.
    public static bool ValidateNewUserDefinedProfileName(string name) => userDefinedProfiles.FirstOrDefault(it => it.Type == ImageRenderingProfileType.UserDefined && it.Name == name) == null;


    // Wait for IO tasks complete.
    public static Task WaitForIOTasksAsync() => ImageRenderingProfile.IOTaskFactory.StartNew(() => logger?.LogDebug("All I/O tasks completed"));
}