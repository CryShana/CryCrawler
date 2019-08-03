# CryCrawler
Portable cross-platform web crawler. Used to crawl websites and download files that match specified criteria.

**Note!** This is a college project. Don't expect this to be actively maintained.

### Features
- **Portable** - Single executable file you can run anywhere without any extra requirements
- **Cross-platform** - Works on Windows/Linux/OSX and other platforms supported by NET Core
- **Multi-threaded** - Single crawler can use as many threads as specified by the user for crawling.
- **Distributed** - Connect multiple crawlers to a host; you can also connect multiple hosts to a single master host. (Using **SSL**)
- **Extensible** - Extend CryCrawler's functionality by using plugins/extensions.
- **Web GUI** - Check status, change configuration, start/stop program, all through your browser - remotely.
- **Breadth/Depth-first crawling** - Two different modes for crawling websites (FIFO or LIFO for storing URLs)
- **Robots Exclusion Standard** - Option for crawler to respect or ignore 'robots.txt' provided by websites
- **Custom User-Agent** - User can provide a custom user-agent for crawler to use when getting websites.
- **File Critera Configuration** - Decide which files to download based on extension, media type, file size or filename.
- **Domain Whitelist/Blacklist** - Force crawler to stay only on certain domains or simply blaclist domains you don't need.
- **Duplicate file detection** - Downloaded files with same names are compared using MD5 checksums to ensure no duplicates.
- **Persistent** - CryCrawler will keep retrying to crawl failed URLs until they are crawled (up to a certain time limit)

### Overview
CryCrawler has two working modes:
- **Crawling mode** (*default*) - program will attempt to crawl provided URLs (provided either locally or by host)
- **Hosting mode** - program will manage multiple connected clients and assign them URLs to crawl

Assuming we are on Linux:
- `./CryCrawler` - Will run CryCrawler in the default *Crawling* mode
- `./CryCrawler -h` (or `./CryCrawler --host`) - Will run CryCrawler in *Hosting* mode
- `./CryCrawler --help` - Will display a list of supported flags (`-d` - Debug mode, `-n` - New session)

### Build
Go into `CryCrawler` directory (path should be `CryCrawler/CryCrawler`) and run the following command:

`dotnet publish -c Release -r win-x64 /p:PublishSingleFile=true  /p:PublishReadyToRun=true`

Replace the `win-x64` with the runtime identifier (RID) that you need:
- Windows (64-bit): `win-x64`
- Windows (32-bit): `win-x86`
- Linux (64-bit): `linux-x64`
- Linux (ARM): `linux-arm`
- Mac OS: `osx-x64`

The executable should now be located in `CryCrawler\bin\Release\netcoreapp3.0\RID\publish`

For other platforms please check the runtime identifier catalog [here](https://docs.microsoft.com/en-us/dotnet/core/rid-catalog).
