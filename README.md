<p align="center"><a href="https://getsharex.com"><img src="https://getsharex.com/img/ShareX_Banner.png" alt="ShareX Banner"/></a></p>
<h3 align="center">Screen capture, file sharing and productivity tool</h3>
<br>
<div align="center">
  <a href="./LICENSE.txt"><img src="https://img.shields.io/github/license/ShareX/ShareX?label=License&color=brightgreen&cacheSeconds=3600" alt="License"/></a>
</div>
<br>
<p align="center"><a href="https://getsharex.com"><img src="https://getsharex.com/img/ShareX_Screenshot.png" alt="ShareX Screenshot"/></a></p>
<p align="center">For further information please check the official <a href="https://getsharex.com">website</a></p>

## About this fork

This is a fork of [Delphox/ShareX](https://github.com/Delphox/ShareX), which itself forks upstream [ShareX/ShareX](https://github.com/ShareX/ShareX) to add **WEBP, AVIF and AV1 support**. All credit for the WEBP/AVIF/AV1 work goes to [Delphox](https://github.com/Delphox) — thank you!

The **JPEG XL (.jxl) support** (encode/decode via libjxl) and the lower minimum video bitrate option are incorporated from [koloved/ShareX](https://github.com/koloved/ShareX/tree/jpegXL) by [koloved](https://github.com/koloved) — with a fix for its JXL decoder event enums, which did not match the libjxl API values and made JXL files fail to decode (only encoding worked).

**Based on upstream ShareX 21.0.0** — all improvements from the official [ShareX 20.2.0 and 21.0.0 releases](https://github.com/ShareX/ShareX/releases) are merged in (new Avalonia-based image editor, OCR and other upstream enhancements), while keeping every fix and addition listed below.

On top of that fork, this repository contains the following changes:

### Fixed
* **AVIF thumbnails failing after a while** — `NativeMethods.CopyMemory` P/Invoke bound to a non-existent `kernel32` export; it is actually `RtlMoveMemory` (`CopyMemory` is a C macro). AVIF files now decode again after the error had permanently poisoned earlier sessions.
* **App crash while loading AVIF images** — `avifDecoderSetIOMemory` was given a pointer to a managed `byte[]` that the GC could move or collect while `libavif` still referenced it. The buffer is now copied to unmanaged memory (`Marshal.AllocHGlobal`) for the decoder's lifetime and freed afterwards.

### Changed
* **Auto-updater now checks this repository** (`readher/ShareX`) instead of Delphox's, so updates come from this fork's releases.
* **Installer build pipeline fixes** — Inno Setup 7 is detected, a locally provided ExifTool zip is used when present, and the extracted ExifTool folder layout is flattened to what the Inno Setup script expects.

### Performance
* **Thumbnail cache** (`ThumbnailCache`) — resized thumbnails are cached in memory (LRU, bounded size) and reused across thumbnail-view refreshes, so screenshots are not re-decoded from disk (AVIF/WebP/JPEG decode is the expensive part) on every refresh or size change.
* **Background cache warm-up** — at startup all recent screenshot files are pre-decoded on a worker thread, so even the *first* scroll through the screenshots grid is smooth instead of stuttering while tiles decode on demand.
* **Smoother scrolling** — removed transparent-background emulation in the thumbnail panels (opaque themed colors instead), enabled double buffering on the scroll containers, fixed mouse wheel scrolling the content twice per notch, removed a redundant full layout pass on every wheel tick, and raised the wheel scroll distance from 48 px to ~144 px per notch to better suit a thumbnail grid.
* **Batched panel layout** — panel additions during startup are batched (`BeginBulkUpdate`/`EndBulkUpdate`) to avoid O(n²) relayouts.

### Added
* **JPEG XL (.jxl) image support** — capture output, thumbnails and file dialogs via libjxl (from [koloved/ShareX](https://github.com/koloved/ShareX/tree/jpegXL), with the decoder enum fix described above).
* **Lower minimum video bitrate** — bitrate fields now allow 1 kbps minimum instead of 100, for tiny/low-motion recordings (from [koloved/ShareX](https://github.com/koloved/ShareX/tree/jpegXL)).

## Links
* Official website: https://getsharex.com
* Upstream ShareX: https://github.com/ShareX/ShareX
* WEBP/AVIF/AV1 fork by Delphox: https://github.com/Delphox/ShareX
* JPEG XL fork by koloved: https://github.com/koloved/ShareX
* Changelog: https://getsharex.com/changelog
* Privacy policy: https://getsharex.com/privacy-policy
* Donate: https://getsharex.com/donate

## Documents
* Image effects: https://getsharex.com/image-effects
* Actions: https://getsharex.com/actions
* Dev builds: https://getsharex.com/docs/dev-builds
* Keybinds: https://getsharex.com/docs/keybinds
* Scrolling screenshot: https://getsharex.com/docs/scrolling-screenshot
* Command line arguments: https://getsharex.com/docs/command-line-arguments
* Translation: https://getsharex.com/docs/translation
* OCR: https://getsharex.com/docs/ocr
* Custom uploader: https://getsharex.com/docs/custom-uploader
* Amazon S3 guide: https://getsharex.com/docs/amazon-s3
* Google Cloud Storage guide: https://getsharex.com/docs/google-cloud-storage
* Cloudflare R2 guide: https://getsharex.com/docs/cloudflare-r2
* Brand assets: https://getsharex.com/brand-assets
