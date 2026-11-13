# Third-party notices

OnlyFenceStoryLine uses these components:

- **SPT 4.1.6 / Escape from Tarkov 0.16.9.5.40743** — the required game environment. Game and SPT assemblies are not included in the source repository.
- **BepInEx and Harmony** — client plugin loading and method patches, referenced from the user's installation.
- **VisitAPI 1.3.7**, by TricolourSky — dialogue, trader visits and the story UI. Install its client and server packages separately: https://github.com/TricolourSky/VisitAPI
- **Storyline Backport**, by TricolourSky — the Fence room, shared room resources and ready-made banner used in the playable archive: https://github.com/TricolourSky/Storyline-Backport

The Storyline Backport project's own files and tools are MIT licensed; the playable archive retains its `Storyline-LICENSE.txt`. Original game images and models originate from Escape from Tarkov by Battlestate Games. That MIT notice does not claim ownership of Battlestate Games assets.

External frameworks are referenced, not copied into the public client/server source. OnlyFence is an optional companion mod. WTT CommonLib and FixPluginTypesSerialization are not dependencies of this release.
