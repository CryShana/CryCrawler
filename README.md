# CryCrawler
Cross-platform distributed multi-threaded web crawler

### Build
Go into `CryCrawler` directory and run the following command (depending on your plaltform):

- Windows (64-bit): `dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Windows (32-bit): `dotnet publish -c Release -r win-x86 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Linux (64-bit): `dotnet publish -c Release -r linux-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
- Linux (ARM): `dotnet publish -c Release -r linux-arm /p:PublishSingleFile=true  /p:PublishReadyToRun=true`
