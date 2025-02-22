# Creating A Medusa

## Getting started

This section will guide you through how to create a simple custom medusa. You can find the entirety of this code hosted [here](https://gitlab.com/nadeko/example_medusa)

#### Prerequisite
- [.net8 sdk](https://dotnet.microsoft.com/en-us/download) installed
- Optional: use [vscode](https://code.visualstudio.com/download) to write code

#### Guide

- Open your favorite terminal and navigate to a folder where you will keep your project .

- Create a new folder and move into it
    - `mkdir example_medusa `
    - `cd example_medusa`

- Install nadeko-medusa template
    - `dotnet new install nadeko-medusa`

- Make a new Nadeko Medusa project
    - `dotnet new nadeko-medusa`

### Build it

- Build your Medusa into a dll that Nadeko can load. In your terminal, type:
    - `dotnet publish -o bin/medusae/example_medusa /p:DebugType=embedded`

- Done. You can now try it out in action.

### Try it out

- Copy the `bin/medusae/example_medusa` folder into your NadekoBot's `data/medusae/` folder. (Nadeko version 4.1.0+)

- Load it with `.meload example_medusa`

- In the channel your bot can see, run the following commands to try it out
    - `.hello` and
    - `.hello @<someone>`

- Check its information with
    - `.meinfo example_medusa`

- Unload it
    - `.meunload example_medusa`

- :tada: Congrats! You've just made your first medusa! :tada:



## Theory

Medusa system allows you to write independent medusae (known as "modules", "cogs" or "plugins" in other software) which you can then load, unload and update at will without restarting the bot.  
  
The system itself borrows some design from the current way Nadeko's Modules are written but mostly from never-released `Ayu.Commands` system which was designed to be used for a full Nadeko v3 rewrite. 

The medusa base classes used for development are open source [here](https://gitlab.com/kwoth/nadekobot/-/tree/v5/src/Nadeko.Medusa) in case you need reference, as there is no generated documentation at the moment.

### Term list

#### Medusa

- The project itself which compiles to a single `.dll` (and some optional auxiliary files), it can contain multiple [Sneks](#snek), [Services](#service), and [ParamParsers](#param-parser)  

#### Snek

- A class which will be added as a single Module to NadekoBot on load. It also acts as a [lifecycle handler](snek-lifecycle.md) and as a singleton service with the support for initialize and cleanup.
- It can contain a Snek (called SubSnek) but only 1 level of nesting is supported (you can only have a snek contain a subsnek, but a subsnek can't contain any other sneks)
- Sneks can have their own prefix
    - For example if you set this to 'test' then a command called 'cmd' will have to be invoked by using `.test cmd` instead of `.cmd` 

#### Snek Command

- Acts as a normal command
- Has context injected as a first argument which controls where the command can be executed
    - `AnyContext` the command can be executed in both DMs and Servers
    - `GuildContext` the command can only be executed in Servers
    - `DmContext` the command can only be executed in DMs
- Support the usual features such as default values, leftover, params, etc.
- It also supports dependency injection via `[inject]` attribute. These dependencies must come after the context and before any input parameters
- Supports `ValueTask`, `Task`, `Task<T>` and `void` return types

#### Param Parser

- Allows custom parsing of command arguments into your own types.
- Overriding existing parsers (for example for IGuildUser, etc...) can cause issues.

#### Service

- Usually not needed.
- They are marked with a `[svc]` attribute, and offer a way to inject dependencies to different parts of your medusa. 
- Transient and Singleton lifetimes are supported.

### Localization

Response and command strings can be kept in one of three different places based on whether you plan to allow support for localization

option 1) `res.yml` and `cmds.yml`  

If you don't plan on having your app localized, but you just *may* in the future, you should keep your strings in the `res.yml` and `cmds.yml` file the root folder of your project, and they will be automatically copied to the output whenever you build your medusa.

##### Example project folder structure:
    - uwu/
        - uwu.csproj
        - uwu.cs
        - res.yml
        - cmds.yml  

##### Example output folder structure:
    - medusae/uwu/  
        - uwu.dll  
        - res.yml  
        - cmds.yml

option 2) `strings` folder  

If you plan on having your app localized (or want to allow your consumers to easily add languages themselves), you should keep your response strings in the `strings/res/en-us.yml` and your command strings in `strings/cmds/en-us.yml` file. This will be your base file, and from there you can make support for additional languages, for example `strings/res/ru-ru.yml` and `strings/cmds/ru-ru.yml`

##### Example project folder structure:
    - uwu/
        - uwu.csproj
        - uwu.cs
        - strings/
            - res/
                - en-us.yml
                - ru-ru.yml
            - cmds/
                - en-us.yml
                - ru-ru.yml

##### Example output folder structure:
    - medusae/uwu/
        - uwu.dll
        - strings/
            - res/
                - en-us.yml
                - ru-ru.yml
            - cmds/
                - en-us.yml
                - ru-ru.yml

option 3) In the code  

If you don't want any auxiliary files, and you don't want to bother making new .yml files to keep your strings in, you can specify the command strings directly in the `[cmd]` attribute itself, and use non-localized methods for message sending in your commands.

If you update your response strings .yml file(s) while the medusa is loaded and running, running `.stringsreload` will reload the responses without the need to reload the medusa or restart the bot.

#### Bot medusa config file

- Medusa config is kept in `data/medusae/medusa.yml` file in NadekoBot installation folder
- At the moment this config only keeps track of which medusae are currently loaded (they will also be always loaded at startup)
- If a medusa is causing issues and you're unable to unload it, you can remove it from the `loaded:` list in this config file and restart the bot. It won't be loaded next time the bot is started up

#### Unloadability issues  

To make sure your medusa can be properly unloaded/reloaded you must:

-  Make sure that none of your types and objects are referenced by the Bot or Bot's services after the DisposeAsync is called on your Snek instances. 

- Make sure that all of your commands execute quickly and don't have any long running tasks, as they will hold a reference to a type from your assembly

- If you are still having issues, you can always run `.meunload` followed by a bot restart, or if you want to find what is causing the medusa unloadability issues, you can check the [microsoft's assembly unloadability debugging guide](https://docs.microsoft.com/en-us/dotnet/standard/assembly/unloadability)


