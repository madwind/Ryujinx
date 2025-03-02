$env:TAG_NAME="release-3.2.6"

git submodule update --init --recursive
git -C src/Ryujinx.SDL3-CS/external/SDL fetch --tags
git -C src/Ryujinx.SDL3-CS/external/SDL checkout tags/$env:TAG_NAME
git add src/Ryujinx.SDL3-CS/external/SDL
git add src/Ryujinx.SDL3-CS/external/update.ps1
git commit -m "Update SDL to tag $env:TAG_NAME"
 
