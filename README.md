# CryCrawler
Cross-platform distributed multi-threaded web crawler

### Build
Go into `CryCrawler` directory and run the following command (depending on your plaltform):

- Windows (64-bit): `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Windows (32-bit): `dotnet publish -c Release -r win-x86 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Linux (64-bit): `dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Linux (ARM): `dotnet publish -c Release -r linux-arm /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Mac OS: `dotnet publish -c Release -r osx-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`

The executable should now be located in `CryCrawler\bin\Release\netcoreapp3.0\PLATFORM\publish`
