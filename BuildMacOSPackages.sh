APP_NAME="PixelViewer"
RID_LIST=("osx-x64" "osx.11.0-arm64")
PUB_PLATFORM_LIST=("osx-x64" "osx-arm64")
CONFIG="Release"

# Reset output directory
rm -r ./Packages
if [ "$?" != "0" ]; then
    exit
fi
mkdir ./Packages
if [ "$?" != "0" ]; then
    exit
fi

# Get application version
VERSION=$(cat ./$APP_NAME/$APP_NAME.csproj | grep "<Version>" | egrep -o "[0-9]+(.[0-9]+)+")
if [ "$VERSION" == "" ]; then
    echo "Unable to get version of $APP_NAME"
    exit
fi
echo " " 
echo "******************** Build $APP_NAME $VERSION ********************"

# Build packages
for i in "${!RID_LIST[@]}"; do
    RID=${RID_LIST[$i]}
    PUB_PLATFORM=${PUB_PLATFORM_LIST[$i]}

    echo " " 
    echo "[$PUB_PLATFORM_LIST ($RID)]"
    echo " "

    dotnet restore $APP_NAME -r $RID
    if [ "$?" != "0" ]; then
        exit
    fi

    dotnet msbuild $APP_NAME -t:BundleApp -property:Configuration=$CONFIG -p:SelfContained=true -p:PublishSingleFile=true -p:PublishTrimmed=true -p:RuntimeIdentifier=$RID
    if [ "$?" != "0" ]; then
        exit
    fi

    mv ./$APP_NAME/bin/$CONFIG/net6.0/$RID/publish/$APP_NAME.app ./Packages/$APP_NAME.app
    if [ "$?" != "0" ]; then
        exit
    fi
    cp ./$APP_NAME/$APP_NAME.icns ./Packages/$APP_NAME.app/Contents/Resources/$APP_NAME.icns
    if [ "$?" != "0" ]; then
        exit
    fi

    zip -r ./Packages/$APP_NAME-$VERSION-$PUB_PLATFORM.zip ./Packages/$APP_NAME.app
    if [ "$?" != "0" ]; then
        exit
    fi

    rm -r ./Packages/$APP_NAME.app
    if [ "$?" != "0" ]; then
        exit
    fi

done

