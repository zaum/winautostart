<h1>
  <img src="logo.svg" width="48" style="vertical-align: middle;" alt="AutoStart Logo">
  AutoStart Manager
</h1>

A lightweight Windows tool to manage startup applications — view, add, remove, and toggle startup items.

<p>
  <img src="screenshot.png" width="600" alt="Screenshot">
</p>

## Features

- **View startup items** — list all programs that run at startup from the registry and the Startup folder
- **Toggle on/off** — enable or disable startup items without deleting them
- **Add items** — search installed apps from the Start Menu, drag & drop `.exe`/`.lnk` files, or browse manually
- **Remove items** — permanently delete startup entries

## Usage

1. Launch the app — all current startup items are displayed
2. Use the checkbox to enable/disable an item
3. Click **🗑** to remove an item
4. Click **📁** to open the file location
5. Type in the **Filter startup items** box to search
6. Search installed apps in the **Search installed apps** box and click **+** to add them
7. Drag & drop a `.exe` or `.lnk` file onto the drop zone

## Requirements

- Windows 7 or later
- .NET 8.0 Runtime

## Build

```bash
dotnet publish -c Release
```
