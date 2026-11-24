# Commons Diversified Uploader – core

The shared .NET library behind the Windows and Linux editions of Commons
Diversified Uploader, a tool for sports photographers who contribute to
[Wikimedia Commons](https://commons.wikimedia.org/). It reads jersey numbers
and jersey colours from match photos, maps them to players through the team
rosters on Wikipedia/Wikidata, proposes the matching Commons categories and
uploads the reviewed photos.

This is a community project. It is not affiliated with or endorsed by the
Wikimedia Foundation.

| Repository | Platform |
| --- | --- |
| `commons-diversified-uploader-macos` | macOS (Swift, own implementation of the same pipeline) |
| `commons-diversified-uploader-linux` | Linux (GTK 4 / libadwaita) |
| `commons-diversified-uploader-windows` | Windows |

All editions share the project file format (`commons-yukleme.json`, stored
next to the photos).

## Contents

- `src/UploaderCore`
  - Florence-2 (base-ft) inference on ONNX Runtime, verified token for token
    against the transformers.js reference
  - tiled photo analysis, roster lookup, matching, title and wikitext builders
  - Commons client: OAuth 2.0 (authorization code with PKCE), chunked upload,
    "depicts" statements
  - `Session`: the UI-independent application logic used by the apps
- `src/UploaderCli` – `cdu-cli` development tool (`fixture`, `analyze`,
  `roster`, `match`)
- `tests/UploaderCore.Tests`

## Building

Requirements: .NET 10 SDK.

```sh
dotnet test
dotnet run --project src/UploaderCli -- roster Q239447
```

The model (about 1 GB) is downloaded from Hugging Face on first use.

## Localisation

Turkish is the source language; the Turkish strings are the keys and English
translations live in `src/UploaderCore/Localization.cs`.

## Privacy

The library talks only to Wikimedia sites and, once, to Hugging Face to
download the model. Photos are analysed on the device. The usage telemetry
built into the official ONNX Runtime binaries is switched off before the
runtime starts.

## Contributing

Commit messages follow the
[Wikimedia commit message guidelines](https://www.mediawiki.org/wiki/Gerrit/Commit_message_guidelines).

## License

[Apache License 2.0](LICENSE)
