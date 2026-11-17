# Building OnlyFenceStoryLine 0.2.0

Use Windows, PowerShell 7.2 or later, the .NET 10 SDK, and your own SPT 4.1.6 installation with VisitAPI 1.3.7 installed on both client and server. The client targets .NET Framework 4.7.2; the server targets .NET 10. The SDK may restore targeting packs on the first build.

Download the matching `OnlyFenceStoryLine-0.2.0-SPT-4.1.6.zip` release, then run from this repository:

```powershell
pwsh -File ./build.ps1 -ReleaseArchive "C:\Downloads\OnlyFenceStoryLine-0.2.0-SPT-4.1.6.zip" -GamePath "C:\Path\To\SPT"
```

This source repository contains the client and server implementation. Localized UI strings and embedded narrative catalogs are distributed only in the playable ZIP, under `build-content/OnlyFenceStoryLine-0.2.0/`. Their hashes are recorded in `build-manifest.json`. The build script checks those hashes, restores the marked string literals into a fresh ignored `.build/run-*/` directory, and builds both projects there. Resource names and string values are preserved. It does not modify the game installation or start the game/server.

The final report and `.build/last-build.json` give the full DLL paths and SHA-256 hashes. Client output ends in `Client/bin/Release/net472/CustomQuests.E17.dll`; server output ends in `Server/bin/Release/net10.0/CustomQuests.E17.Server.dll`. Build inputs can be prepared without compiling by adding `-PrepareOnly`.

Edit the public `Client/*.cs` and `Server/*.cs` files to change the implementation, and rerun the same command. Keep `/* release-text:... */ ""` markers intact unless deliberately replacing that string. The build reports modified source files while allowing local development. Direct `dotnet build` on the public projects is blocked to prevent compiling empty content placeholders.

For maintainers preparing a release, `-ContentDirectory` can replace `-ReleaseArchive` and point to the four matching build-content files before the ZIP exists. Release DLLs are built by this same script. Unmodified source hashes are checked against the manifest, and compiler paths are normalized. Exact binary reproduction also requires matching SDK and reference assemblies; the SDK version is included in the build report.

The build produces two DLLs. Installation still requires the entire matching playable release. Do not commit `.build/`, content files, game assemblies, or generated binaries.
