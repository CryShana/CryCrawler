# CryCrawler
Cross-platform distributed multi-threaded web crawler

### Build
Go into `CryCrawler` directory and run the following command:

`dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`

Replace the `win-x64` with the runtime identifier (RID) that you need:
- Windows (64-bit): `win-x64`
- Windows (32-bit): `win-x86`
- Linux (64-bit): `linux-x64`
- Linux (ARM): `linux-arm`
- Mac OS: `osx-x64`

The executable should now be located in `CryCrawler\bin\Release\netcoreapp3.0\RID\publish`

For other platforms please check the runtime identifier catalog [here](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog).
