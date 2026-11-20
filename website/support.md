# Support

Support for Hostline RD is provided through GitHub Issues:

**[github.com/blnahmt/hostline/issues](https://github.com/blnahmt/hostline/issues)**

Before opening a new issue, please search the existing ones. When reporting a bug, include your Windows version, the Hostline RD version and the steps to reproduce the problem. Do not post passwords, private keys or other secrets in an issue.

## Frequently asked questions

### What do I need to run Hostline RD?

- Windows 10 or 11 (x64)
- Nothing else: the installer includes the .NET runtime and needs no administrator rights.
- Microsoft Edge WebView2 Runtime, needed only for SSH tabs. It is already built into Windows 11.

### Where is my data stored?

Everything is stored locally in `%AppData%\HostlineRD`: connections, settings, history, thumbnails and saved SSH host keys. Saved passwords are encrypted with Windows DPAPI for your Windows user.

### How do I back up my connections?

Use **Export**. Set a passphrase to encrypt the file (AES-256-GCM) and include your saved passwords; without one, passwords are left out. Use **Import** to load the file again.

### I cannot find the tray icon.

On Windows 11 the icon may be hidden in the overflow area of the taskbar (the arrow next to the clock). Drag it from there onto the taskbar, or turn it on in Windows **Settings > Personalization > Taskbar > Other system tray icons**.

### How do I get updates?

The app checks GitHub Releases for new versions automatically. You can turn this off in **Settings > Updates** and download new versions from the [releases page](https://github.com/blnahmt/hostline/releases/latest) yourself.
