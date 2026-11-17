# OnlyFenceStoryLine

A mod for **SPT 4.1.6**. **VisitAPI 1.3.7 is required** (client and server).

**Beta 0.2.0 · English, Russian and Korean localization**

**[Download Beta 0.2.0](https://github.com/Klidevich/OnlyFenceStoryLine/releases/tag/v0.2.0)**

**Installation now requires two packages. Download and extract both into your SPT folder:** [Main package](https://github.com/Klidevich/OnlyFenceStoryLine/releases/download/v0.2.0/OnlyFenceStoryLine-0.2.0-SPT-4.1.6.zip) · [Additional rooms](https://github.com/Klidevich/OnlyFenceStoryLine/releases/download/v0.2.0/OnlyFenceStoryLine-0.2.0-AdditionalRooms.zip)

**A new SPT profile is required for this beta. Do not use an existing profile.**

![Fence visit](https://raw.githubusercontent.com/Klidevich/OnlyFenceStoryLine/main/image.png)

## What's new in Beta 0.2.0

- Expanded the storyline from 4 to 22 quests, with branching choices along the way.
- Added visits to more traders, with four visit rooms across the storyline.
- Reworked dialogue and journal entries to follow what your character has actually discovered, read, and discussed.
- Reworked conversation resumption and quest-item recovery.
- Installation now requires two packages: the main mod and additional rooms. Both are required.

## Requirements

- **SPT 4.1.6**, using **EFT 0.16.9.5.40743**.
- **[VisitAPI 1.3.7](https://github.com/TricolourSky/VisitAPI/releases)**: install both its client and server components first.
- **[OnlyFence](https://github.com/Klidevich/OnlyFence) is optional but recommended.** It is not required to install or run OnlyFenceStoryLine.

The two release archives include the room resources used by this mod. Installing the entire Storyline Backport content pack is not required. WTT CommonLib and FixPluginTypesSerialization are not used.

## Installation

1. Close the game and SPT server. Back up your profile and any existing files this mod replaces.
2. Install the required VisitAPI client and server components.
3. Download **both** `OnlyFenceStoryLine-0.2.0-SPT-4.1.6.zip` and `OnlyFenceStoryLine-0.2.0-AdditionalRooms.zip` from [Releases](https://github.com/Klidevich/OnlyFenceStoryLine/releases).
4. Extract **both archives** into your SPT installation folder, alongside `EscapeFromTarkov.exe`. The main package contains the mod, Fence's room and shared resources; the additional archive contains the Ragman, Therapist and Prapor rooms required by the questline.
5. Start the SPT server. Create and select a **new profile** in the SPT Launcher, then start the game and create your character.

The main installed paths are:

```text
BepInEx/plugins/CustomQuests-E17/CustomQuests.E17.dll
BepInEx/config/VisitAPI/*.dlg
BepInEx/plugins/VisitAPI/rooms/
SPT_Runtime/user/mods/CustomQuests-E17/CustomQuests.E17.Server.dll
SPT_Runtime/user/mods/VisitAPI-Server/packs/CustomQuests-E17/
```

If your VisitAPI server folder has a different name, put `packs/CustomQuests-E17` inside its actual `packs` directory. Do not create a second VisitAPI installation or a nested `rooms/rooms` folder. The internal folder names above are retained for compatibility.

This mod supplies trader dialogue files. Back up the affected files if another mod changes them; automatic merging is not supported. Shared room resources may also be used by other VisitAPI content.

Install both archives, including **both DLLs, all dialogue/content files, and all rooms**. Do not leave duplicate copies of either DLL in another plugin/mod folder. A new profile is mandatory for beta testing, including when you have tried an earlier development build.

## Beta status and feedback

This is a beta release. In-game testing is in progress.

Please report the release version, game language, confirmation that you created a new profile, the steps taken, and the expected and actual results. Include relevant excerpts from `BepInEx/LogOutput.log` and the server log. Do not post full profiles or public story spoilers; contact the maintainer before sharing narrative details or screenshots containing them.

## Build the source

Install **PowerShell 7.2+** and the **.NET 10 SDK**, then build against your own SPT installation with VisitAPI already installed and the matching release ZIP:

```powershell
pwsh -File ./build.ps1 -ReleaseArchive "C:\Downloads\OnlyFenceStoryLine-0.2.0-SPT-4.1.6.zip" -GamePath "C:\Path\To\SPT"
```

The script reports both DLL paths in `.build/last-build.json`. Localized strings and embedded narrative resources are restored from the matching ZIP into the ignored build directory. Narrative data and game assemblies are not included in this source repository. The build does not deploy files into the game. See [build instructions](BUILD.md) for details.

## Credits

- **Klide** — creator, developer and maintainer.
- **ChatGPT / Codex (OpenAI)** — AI-assisted development, debugging, review and documentation.
- **TricolourSky** — VisitAPI and the Storyline Backport resources used by this release.
- **Battlestate Games** — original Escape from Tarkov assets.

See [Third-party notices](THIRD-PARTY-NOTICES.md). Not affiliated with Battlestate Games or the SPT team.
