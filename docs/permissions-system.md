# Permissions Overview

Have you ever felt confused or even overwhelmed when trying to set Nadeko's permissions? In this guide we will be explaining **how to use the permission commands correctly** and even **cover a few common questions**! Every command we discuss here can be found in the [Commands List].

## Why do we use the Permissions Commands?

Permissions are very handy at setting who can use what commands in a server. All commands and modules are enabled by default. If something is a bot owner only command, it can only be ran by the bot owner, the person who is running the bot, or has their ID in the [creds.yml](creds-guide.md) file.

Several commands still require that you have the correct permissions on Discord to be able to use them, so for users to be able to use commands like `.kick` and `.voicemute`, they need **Kick** and **Mute Members** server permissions, respectively.

With the permissions system it possible to restrict who can skip the current song.

## First Time Setup

To change permissions you **must** meet the following requirements:

**Have Administrator Server Permission.**

**If you are NOT the server owner or an admin, get the role set to `.permrole` (there is no permission role by default).**

## Basics & Hierarchy

The [Commands List] is a great tool which lists **all** available commands, however we'll go over a few of them here.

First, let's explain how the permissions system works - It's simple once you figure out how each command works!
The permissions system works as a chain. Everytime a command is used, the permissions chain is checked. Starting from the top of it, the command is compared to a rule, if it isn't either allowed or disallowed by that rule it proceeds to check the next rule all the way till it reaches the bottom rule, which allows all commands.

To view this permissions chain, do `.lp`. The rule at the top of the chain takes priority over all rules below it.

If you want to remove a permission from the chain of permissions, do `.rp X` to remove rule number X and similarly, do `.mp X Y` to move rule number X to number Y (moving, not swapping!).

If you want the bot to notify users why they can't use a command or module, use `.verbose true` and Nadeko will tell you what rule is preventing the command from being used.

## Commonly Asked Questions

#### How do I restrict all commands to a single channel?

To allow users to only use commands in a specific text channel, follow these steps:

1. `.asm disable`
    - Disables all modules on the entire server
2. `.acm enable #bot-spammerino`
    - Enables all modules in the #bot-spammerino channel

#### How do I allow only one module to be used in a specific channel?

To allow users to only use commands from a certain module, let's say **gambling**, in a specific text channel, follow these steps:

1. `.acm disable #gamblers-den`
    - Disables all modules in the #gamblers-den channel
2. `.cm Gambling enable #gamblers-den`
    - Enables usage of the Gambling module in the #gamblers-den channel

#### How do I create a music DJ?

To allow users to only see the current song and have a DJ role for queuing follow these steps:

1. `.sm Music disable`
    - Disables music commands for everybody
2. `.sc .nowplaying enable`
    - Enables the "nowplaying" command for everyone
3. `.sc .listqueue enable`
    - Enables the "listqueue" command for everyone
4. `.rm Music enable DJ`
    - Enables all music commands only for the DJ role

#### How do I disable Expressions from triggering?

If you don't want server or global Expressions, just block the module that controls their usage:

1. `.sm ActualExpressions disable`
    - Disables the ActualExpression module from being used

**Note**: The `Expressions` module controls the usage of Expressions. The `Expressions` module controls commands related to Expressions (such as `.acr`, `.lcr`, `.crca`, etc).

#### I've broken permissions and am stuck, can I reset permissions?

Yes, there is a way, in one easy command!

1. `.resetperms`
    - This resets the permission chain back to default

*-- Thanks to @applemac for providing the template for this guide*

[Commands List]: https://nadeko.bot/commands
