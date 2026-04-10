# S3 Lite

A lightweight, fast S3 browser for Windows built with C# + WinForms + .NET 10.

## Features

### Browsing
- Connect to AWS S3 or any S3-compatible storage (MinIO, Backblaze B2, Cloudflare R2, etc.)
- Browse buckets and folders with file icons, type descriptions, and storage class
- Mouse back/forward navigation, keyboard shortcuts (Backspace, Delete, Ctrl+A, Ctrl+U)
- Real-time file search/filter
- Dark and Light theme

### File Operations
- Upload files (toolbar, drag & drop from Explorer)
- Download files and folders
- Delete files and folders
- Rename files and folders (server-side)
- New folder creation
- Copy / Move files between buckets (server-side, no bandwidth used)

### Transfers
- Parallel transfer queue with separate concurrency limits for uploads and downloads
- Multipart upload with configurable parallel parts and threshold
- Pause / Resume / Cancel per transfer job
- Transfer speed and progress display

### GZip Compression
- Rule-based GZip compression before upload (wildcard bucket + file mask matching)
- Automatically sets `Content-Encoding: gzip` so browsers decompress transparently
- Compression ratio shown in File Properties

### File Preview
- Double-click or right-click → Preview
- Images: PNG, JPG, WEBP, GIF, BMP and more (up to configurable size limit)
- Text: TXT, JSON, HTML, CSS, JS, SQL, and more (first 512 KB)
- Configurable max preview size

### Bucket Management
- Create buckets (with Object Lock, Block Public Access, Disable ACLs options)
- Delete buckets (empties all objects first)
- Bucket Properties dialog (size, object count, versioning, encryption, replication, etc.)
- Change storage class for all files in a bucket
- Add external buckets with auto-detect region

### File Properties
- Right-click → Properties: ETag, size, storage class, last modified, encryption status
- Shows compression ratio and original file size for GZip-compressed files
- Owner, version ID, custom metadata

### Permissions & URLs
- Edit ACL permissions (Owner / Authenticated Users / Everyone)
- Make Public / Make Private presets
- Generate pre-signed web URLs with custom expiry and hostname options

### Other
- Saved connection profiles
- Auto-connect to last used profile on startup
- System tray icon with minimize-to-tray support
- Restore objects from Glacier / Deep Archive

## Requirements

- Windows 10 / 11
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Desktop)

## Getting Started

1. Download `S3Lite.exe` from [Releases](../../releases)
2. Run `S3Lite.exe`
3. Click **Connect** and enter your AWS credentials or select a saved profile

## Building from Source

```
dotnet publish -c Release -o publish_lite
```

Requires .NET 10 SDK.

## Tech Stack

- C# / WinForms / .NET 10
- AWS SDK for .NET v4
- Single-file publish (~4 MB, framework-dependent)
