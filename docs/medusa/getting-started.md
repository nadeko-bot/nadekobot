## Getting Started

### What is the Medusa system?

- It is a dynamic module/plugin/cog system for NadekoBot introduced in **NadekoBot 4.1.0**  

- Allows developers to add custom functionality to Nadeko without modifying the original code  

- Allows for those custom features to be updated during bot runtime (if properly written), without the need for bot restart.

- They are added to `data/medusae` folder and are loaded, unloaded and handled through discord commands.  
    - `.meload` Loads the specified medusa (see `.h .meload`)
    - `.meunload` Unloads the specified medusa (see `.h .meunload`)
    - `.meinfo` Checks medusae information (see `.h .meinfo`)
    - `.melist` Lists the available medusae (see `.h .melist`)

### How to make one?

Medusae are written in [C#](https://docs.microsoft.com/en-us/dotnet/csharp/tour-of-csharp/) programming language, so you will need at least low-intermediate knowledge of it in order to make a useful Medusa.

Follow the [creating a medusa guide](creating-a-medusa.md)

### Where to get medusae other people made?

⚠ *It is EXTREMELY, and I repeat **EXTREMELY** dangerous to run medusae of strangers or people you don't FULLY trust.* ⚠  
⚠ *It can not only lead to your bot being stolen, but it also puts your entire computer and personal files in jeopardy.* ⚠

**It is strongly recommended to run only the medusae you yourself wrote, and only on a hosted VPS or dedicated server which ONLY hosts your bot, to minimize the potential damage caused by bad actors.**

No easy way at the moment, except asking in the `#dev-and-modding` chat in [#NadekoLog server](https://discord.nadeko.bot)

