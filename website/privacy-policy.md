# Privacy Policy

_Last updated: 20 November 2026_

Hostline RD is a desktop app for Windows. It has no account, no analytics and no telemetry. This page explains what data the app keeps and which network connections it makes.

## Data stored on your computer

All app data is stored locally in `%AppData%\HostlineRD` on your computer. This includes:

- your saved connections
- your settings
- your connection history
- session thumbnails
- saved SSH host keys

Saved passwords are encrypted with Windows DPAPI for the current Windows user. The app only uses them to sign in to the servers you connect to.

None of this data is sent to the developer or to any third party.

## No account, analytics or telemetry

You do not need an account to use Hostline RD. The app does not collect usage statistics, crash reports or any other telemetry.

## Network connections the app makes

The app itself only makes these network connections:

1. **Connections you open.** RDP, VNC and SSH connections to the servers you configure.
2. **Reachability check.** A periodic TCP connection attempt to the port of each configured server, used to show the green/red reachability indicator.
3. **Update check.** When automatic update checks are on, the app downloads a small file (`latest.json`) from github.com to see whether a new version is available. You can turn this off in **Settings > Updates**.

## Downloads from GitHub

Hostline RD is downloaded and updated from GitHub. GitHub's own privacy policy applies to those downloads.

## Exported files

If you use **Export**, the app writes your connection list to a location you choose. If you set a passphrase, the file is encrypted (AES-256-GCM) and includes your saved passwords; without a passphrase, passwords are left out. Where you keep or share that file is up to you.

## Removing your data

See [Delete your data](delete-account).

## Questions

Please open an issue on [GitHub](https://github.com/blnahmt/hostline/issues).
