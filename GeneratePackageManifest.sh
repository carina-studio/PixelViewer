APP_NAME="PixelViewer"

echo "********** Start generating package manifest of $APP_NAME **********"

# Get application version
VERSION=$(dotnet run PackagingTool.cs -- get-current-version $APP_NAME/$APP_NAME.csproj)
if [ "$?" != "0" ]; then
    echo "Unable to get version of $APP_NAME"
    exit
fi
INFORMATIONAL_VERSION=$(dotnet run PackagingTool.cs -- get-current-informational-version $APP_NAME/$APP_NAME.csproj)
if [ "$?" != "0" ]; then
    echo "Unable to get informational version of $APP_NAME"
    exit
fi
echo "Version: $VERSION ($INFORMATIONAL_VERSION)"

# Generate package manifest
dotnet run PackagingTool.cs -- create-package-manifest $APP_NAME $VERSION $INFORMATIONAL_VERSION
if [ "$?" != "0" ]; then
    exit
fi

# Rename to match URI in App.axaml.cs and duplicate for preview channel
mv -f Packages/$VERSION/PackageManifest.json Packages/$VERSION/PackageManifest-v2.json
if [ "$?" != "0" ]; then
    exit
fi
cp -f Packages/$VERSION/PackageManifest-v2.json Packages/$VERSION/PackageManifest-Preview-v2.json
