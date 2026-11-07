# Contributing to NadekoBot

- Report a bug in [GitHub issues](https://github.com/nadeko-bot/nadekobot/issues). If you are not sure that it is a bug, ask in the [NadekoBot Discord server](https://discord.nadeko.bot/) first.
- Suggest a feature on [nadeko.bot/suggest](https://nadeko.bot/suggest).
- To change the code, build it with the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) as in the [source guide](https://docs.nadeko.bot/guides/source-guide/), and run `dotnet test` before you open a pull request.
- If you add or change a command, regenerate the command list, or CI fails: `dotnet run -c Release --project src/NadekoBot -- --commandlist data/commandlist.json`.

We love your input! We want to make contributing to this project as easy as possible, whether it's:

- Reporting a bug
- Discussing the current state of the code
- Submitting a fix
- Proposing new features
- Becoming a maintainer

## Contribution

By submitting code, content, or materials via pull request or similar means ("Contribution"), you irrevocably assign all
intellectual property rights (including copyright and patents) to NadekoBot Repository Owner and affirm you either:

- (a) own the Contribution outright, or
- (b) it is licensed under compatible terms permitting unrestricted relicensing.

You grant the NadekoBot Repository Owner perpetual, worldwide rights to use, modify, distribute, and sublicense the
Contribution under AGPLv3, a commercial license, or any other terms without compensation.

These terms survive termination of this agreement.
