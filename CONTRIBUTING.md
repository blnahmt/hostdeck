# Contributing to Hostline RD

Thanks for helping! Bug reports, ideas and pull requests are welcome.

## Before you start

- **Bugs and ideas:** open an [issue](https://github.com/blnahmt/hostline/issues). Never post passwords, private keys or real server addresses.
- **Larger changes:** open an issue first so we can agree on the approach before you spend time on it.

## License and CLA

Hostline RD is **source-available** under the [PolyForm Strict License 1.0.0](LICENSE): you can read and use the code for noncommercial purposes, and you may fork and change it **only to prepare pull requests to this repository**. Redistributing the app or a modified version is not allowed, so there is a single official release.

Before your first pull request can be merged, you will be asked to sign the [Contributor License Agreement](CLA.md). The CLA Assistant bot posts a link on the pull request; signing takes one click with your GitHub account.

## Development

- Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- `dotnet build RdpManager.sln` and `dotnet test src/RdpManager.Tests`
- Architecture notes and known pitfalls (in Turkish): [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)

Please keep changes focused, match the existing code style, and add or update tests for new logic. User-visible text goes through `Loc.T` / `{loc:T '...'}`, with the English translation added to `src/RdpManager/Localization/en/*.tsv`.
