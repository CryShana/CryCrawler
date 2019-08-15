# CryCrawler
Portable cross-platform web crawler. Used to crawl websites and download files that match specified criteria.

**Note!** This is a college project. Don't expect this to be actively maintained. (I also don't recommend using it for any complex and large crawling projects)

## Features

- **Portable** - Single executable file you can run anywhere without any extra requirements
- **Cross-platform** - Works on Windows/Linux/OSX and other platforms supported by NET Core
- **Multi-threaded** - Single crawler can use as many threads as specified by the user for crawling.
- **Distributed** - Connect multiple crawlers to a host; or connect multiple hosts to a master host. (using **SSL**)
- **Extensible** - Extend CryCrawler's functionality by using plugins/extensions.
- **Web GUI** - Check status, change configuration, start/stop program, all through your browser - remotely.
- **Breadth/Depth-first crawling** - Two different modes for crawling websites (FIFO or LIFO for storing URLs)
- **Robots Exclusion Standard** - Option for crawler to respect or ignore 'robots.txt' provided by websites
- **Custom User-Agent** - User can provide a custom user-agent for crawler to use when getting websites.
- **File Critera Configuration** - Decide which files to download based on extension, media type, file size, filename or URL.
- **Domain Whitelist/Blacklist** - Force crawler to stay only on certain domains or simply blaclist domains you don't need.
- **Duplicate file detection** - Files with same names are compared using MD5 checksums to ensure no duplicates.
- **Persistent** - CryCrawler will keep retrying to crawl failed URLs until they are crawled (up to a certain time limit)

## Overview

CryCrawler has two working modes:
- **Crawling mode** (*default*) - program will attempt to crawl provided URLs (provided either locally or by host)
- **Hosting mode** - program will manage multiple connected clients and assign them URLs to crawl

Assuming we are on Linux:
- `./CryCrawler` - Will run CryCrawler in the default *Crawling* mode
- `./CryCrawler -h` (or `./CryCrawler --host`) - Will run CryCrawler in *Hosting* mode
- `./CryCrawler --help` - Will display a list of supported flags (`-d` - Debug mode, `-n` - New session)

Running it for the first time will generate the following:
- `config.json` - main configuration file for CryCrawler
- `plugins/` - folder where you can place your plugins to be loaded on startup
- `plugins/PluginTemplate.cs-template` - plugin template file for creating plugins
- `crycrawler_cache` - this file stores all crawled URLs and backups of current URLs to be crawled.

#### Crawling mode
CryCrawler will start crawling available URLs immediately. 

By default, if no previous URLs are loaded, it will use seed Urls (defined in `config.json` or via WebGUI) to start.

However, **if you are using Host for URLs** - locally defined URLs will be ignored and any existing crawled data will be erased. This is because Host manages all crawled URLs and work.


#### Hosting mode
CryCrawler starts listening for connections. URLs will start being crawled only once at least one client is connected. 

Loading URLs works the same way as it works in *Crawling* mode.

## Quick Start

1. Run CryCrawler normally (by double-clicking it or running `./CryCrawler`)

2. Access CryCrawler's WebGUI by opening your browser and navigating to `127.0.0.1:6001`

3. Scroll down to `Seed Urls` and enter the URLs you wish to start with (one per line)

4. You can adjust any other configuration options here (but if you want a more extensive configuration, check `config.json`)

5. Click `Update Configuration` once done and CryCrawler will load up the URLs and start crawling

6. Remember to properly shut it down by using `Ctrl + C` (to ensure all current uncrawled URLs are backed up to the cache)

## Configuration
The `config.json` has 3 main parts (`HostConfig`, `WorkerConfig` and `WebGUI`)

- **HostConfig** - Configuration used by the Host (CryCrawler in Hosting mode)
  - **ListenerConfiguration** - Configure the listening endpoint and optionally define password
  - **MaxClientAgeMinutes** - Max. allowed time for a client to be offline before Host removes it from it's client list
  - **ClientWorkLimit** - Max. number of found URLs a client can accumulate before it needs to send them to the host
- **WorkerConfig**
  - **HostEndpoint** - Host endpoint to connect to. (set `UseHost` to `true` to use this endpoint)
  - **DownloadsPath** - Relative path to the folder where all files will be downloaded to
  - **DontCreateSubfolders** - If `false`, subfolders will be created for each domain and path of file
  - **LogEveryCrawl** - If `true`, every URL crawl will be written to the console
  - **MaxConcurrency** - Max. number of threads the crawler can use for crawling
  - **DepthSearch** - If `true`, it will crawl URLs depth-first - recommended is `false`
  - **MaxLoggedDownloads** - Max. number of recently downloaded items to track and display on Web GUI
  - **MaxFileChunkSizekB** - Max. file chunk size (in kB) when transferring files to Host in chunk
  - **MaxCrawledWorksBeforeCleanHost** - Max. number of crawled works to store before clearing them
  - **AutoSaveIntervalSeconds** - Define interval when to backup uncrawled URLs in memory to disk cache.
  - **UserAgent** - User-Agent the crawler will use when visiting websites (can not be empty)
  - **RespectRobotsExclusionStandard** - If `true`, crawler will try to get `robots.txt` from every website and respect it.
  - **AcceptedExtensions** - (File Criteria) List of accepted extensions (ex: `.jpg`)
  - **AcceptedMediaTypes** - (File Criteria) List of accepted media types (ex: `image/jpeg`)
  - **ScanTargetsMediaTypes** - Media types that should be scanned for more URLs (ex: `text/html`)
  - **DomainWhitelist** - List of whitelisted domains. If this list is not empty, only these domains will be crawled.
  - **DomainBlacklist** - List of blacklisted domains. Any domain listed here will be ignored.
  - **FilenameMustContainEither** - (File Criteria) List of words - filename must contain at least one of them. Case insensitive.
  - **URLMustMatchPattern** - (File Criteria) List of URL patterns - filename URL must match at least on of them. Case insensitive.
  - **MaximumAllowedFileSizekB** - (File Criteria) Max. allowed file size in kB (`-1` for no limit)
  - **MinimumAllowedFileSizekB** - (File Criteria) Min. allowed file size in kB (`-1` for no limit)
  - **AcceptAllFiles** - If `true`, ignores all file criteria and downloads every file it finds.
  - **Urls** - List of seed URLs that will be loaded when cache is empty (no previous URLs can be loaded)
- **WebGUI** - Configure the listening endpoint for the Web GUI

## Build

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
