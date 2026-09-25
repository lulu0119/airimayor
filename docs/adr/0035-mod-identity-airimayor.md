# Mod identity is airimayor

The mod ships as `airimayor`: one lowercase id for the mod folder, the assembly (`airimayor.dll`), the C# namespace (`airimayor` / `airimayor.Host`), the Gameface binding group, the chat locale prefix (`airimayor.Chat.*`), and the on-disk trees (`Mods/airimayor`, `ModsData/airimayor`). The player-facing display name stays `AIRI Mayor`. There is no migration: the product has not shipped, so the old `CitiesSkylines2Agent` settings and runtime trees are orphaned and removed by hand.

Considered options: a PascalCase `AiriMayor` namespace beside a lowercase `airimayor` mod id follows C# convention, but two casings for one identity invite drift between the embedded catalog name, the binding group, and the deploy folder. One literal id everywhere is the simpler seam.

Consequences: the old `CitiesSkylines2Agent` settings and runtime trees are orphaned with no code migration; the local `.coc` settings file was carried over by hand so the endpoint, key, and model survive.
