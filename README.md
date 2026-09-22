# AIRI Mayor

![AIRI Mayor](docs/images/banner.png)

> Learn more at [Project AIRI](https://github.com/moeru-ai/airi) and the [live demo](https://airi.moeru.ai).

**AIRI Mayor** plays **Cities: Skylines II**. A Gameface chat in the game talks to a C# loop that builds on the simulation thread. There is no external agent process. Not listed on Paradox Mods yet; the intended install is the mod store plus an API key.

- Vocabulary: [CONTEXT.md](./CONTEXT.md)
- Decisions: [docs/adr/](./docs/adr/)
- Open work: [docs/open-work.md](docs/open-work.md)
- Agent rules: [AGENTS.md](./AGENTS.md)
- Docs index: [docs/README.md](./docs/README.md)

## Build

Windows + the game. Full setup: [Windows onboarding](docs/guide/2026-08-06-windows-onboarding.md).

```bash
cd Mod
dotnet build
```

Enable **CitiesSkylines2Agent**, load a save; chat is bottom-right. UI-only: `cd Mod/UI && npm run build`. Offline POCs: [archive/](./archive/README.md).

## License

[Apache License 2.0](./LICENSE). The tool layer is an inlined adaptation of [CS2MCP](https://github.com/LancerComet/cities-skylines-2-mcp); attribution is in [NOTICE](./NOTICE). Portions of `CreateDefinitions.cs` remain under the Paradox Interactive EULA.
