---
title: Creating a Medusa
description: Create a NadekoBot medusa with the dotnet template. Build it, load it in your bot, and learn sneks, commands, parsers, services, strings and the lifecycle.
---

# Creating a Medusa

This page shows how to make, build and load a medusa. The project template has example commands. You can see the template [on GitHub](https://github.com/nadeko-bot/nadekobot/tree/v6/src/Nadeko.Medusa.Template/content/nadeko-medusa).

## Before you start

- Install the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).
- Use a NadekoBot which you host yourself, version 7 or later.
- Optional: install a code editor, for example [Visual Studio Code](https://code.visualstudio.com/).

## Make the project

1. Open a terminal and go to the folder for your projects.
2. Make a folder for the medusa and go into it:

    ```sh
    mkdir example_medusa
    cd example_medusa
    ```

3. Install the template from the NadekoBot MyGet feed:

    ```sh
    dotnet new install nadeko-medusa --nuget-source https://www.myget.org/F/nadeko/api/v3/index.json
    ```

4. Make the project:

    ```sh
    dotnet new nadeko-medusa
    ```

The project uses the newest `Nadeko.Medusa` 7 package. To use the package version of your bot, open `example_medusa.csproj` and change `7.*` to your bot version, for example `7.8.0`.

## Build and load the medusa

1. Build the medusa:

    ```sh
    dotnet publish -o bin/medusae/example_medusa
    ```

2. Copy the folder `bin/medusae/example_medusa` into the `data/medusae` folder of your bot. The folder and the `.dll` file must have the same name: `data/medusae/example_medusa/example_medusa.dll`.
3. In Discord, type `.meload example_medusa`.
4. Try the example commands:
    - `.hello`
    - `.hello @someone`
    - `.info John,24`
5. Type `.meinfo example_medusa` to see its sneks and its number of commands.
6. Type `.meunload example_medusa` to unload it.

## Parts of a medusa

| Part | What it is |
|---|---|
| Medusa | The project. It builds into one `.dll` file, with optional extra files. It can have sneks, param parsers and services. |
| Snek | A class which becomes one module of the bot. Each snek has one instance while the medusa is loaded. |
| Snek command | A method with the `[cmd]` attribute. It works like a normal bot command. |
| Param parser | A class which turns the text of an argument into your own type. |
| Service | A class with the `[svc]` attribute, which you can inject into sneks and commands. |

The base classes are open source in [Nadeko.Medusa](https://github.com/nadeko-bot/nadekobot/tree/v6/src/Nadeko.Medusa).

### Snek

- A snek can have one level of sub-sneks. A sub-snek is a nested class in the snek class. It cannot have more sneks in it.
- A snek can have a prefix. If the prefix is `test`, the command `cmd` becomes `.test cmd`.

### Snek command

- The first parameter is the context. It sets where the command works:
    - `AnyContext`: in servers and in direct messages
    - `GuildContext`: only in servers
    - `DmContext`: only in direct messages
- Parameters with the `[inject]` attribute come after the context and before the normal parameters. The bot puts services in them.
- Commands support default values, `[leftover]` and `params`.
- `[user_perm]` and `[bot_perm]` set the permissions which a command needs. `[bot_owner_only]` makes a command owner-only.
- A command can return `ValueTask`, `Task`, `Task<T>` or `void`. The bot does not load a command which returns `ValueTask<T>`.

### Param parser

A param parser inherits from `ParamParser<T>`. The template has `EmployeeInfoParser`, which turns `John,24` into an `EmployeeInfo`. The bot keeps its own parsers. If your parser is for a type which the bot already parses (for example `IGuildUser`), the bot ignores your parser. Use your own types.

### Service

Most medusae do not need services. Mark a class with `[svc(Lifetime.Singleton)]` or `[svc(Lifetime.Transient)]`, then inject it with `[inject]`.

## Strings

Keep the response strings and the command descriptions in one of three places:

- **`res.yml` and `cmds.yml`** in the project folder. Use this if you do not need translations now. The build copies the files to the output.
- **The `strings` folder**, for translations: `strings/res/en-us.yml` and `strings/cmds/en-us.yml`, and one more file in each folder for each language, for example `strings/res/ru-ru.yml`.
- **In the code.** Put the command description in the `[cmd]` attribute, and send text which is not translated.

If `res.yml` exists, the bot ignores the `strings/res` folder. If `cmds.yml` exists, the bot ignores the `strings/cmds` folder. The template has both files, so delete them when you change to the `strings` folder.

When you change a response file of a loaded medusa, type `.stringsreload`. The bot loads the new strings without a reload of the medusa.

## Lifecycle

A snek can override these methods:

| Method | When the bot calls it |
|---|---|
| `InitializeAsync` | When the bot loads the medusa. |
| `DisposeAsync` | When the bot unloads the medusa. |
| `ExecOnMessageAsync` | First, for each message. Return `true` to stop the bot from processing the message. |
| `ExecInputTransformAsync` | Next. Return the new text of the message, or `null` to keep it. The bot looks for a command in the new text. |
| `ExecPreCommandAsync` | After the bot finds a command (of any module), before it runs. Return `true` to stop the command. |
| `ExecPostCommandAsync` | After a command runs without an error. |
| `ExecOnNoCommandAsync` | When no command runs: the bot finds no command, or a check stops the command. The bot calls this method or `ExecPostCommandAsync`, never both. |

## Unload problems

The bot can unload a medusa only when nothing holds a reference to its code. To keep your medusa unloadable:

- In `DisposeAsync`, remove each reference which the bot or its services have to your objects (for example event handlers).
- Keep commands short. A long task holds a reference to your code.

If a medusa does not unload, type `.meunload`, then restart the bot. To find the cause, read the [.NET guide to assembly unloadability](https://learn.microsoft.com/dotnet/standard/assembly/unloadability).

If a medusa breaks the bot and you cannot unload it, remove its name from the `loaded` list in `data/medusae/medusa.yml`, then restart the bot.
