// global using System.Collections.Concurrent;
global using NonBlocking;

// packages
global using Serilog;

// nadekobot
global using NadekoBot;
global using NadekoBot.Db;
global using NadekoBot.Services;
global using Nadeko.Common; // new project
global using NadekoBot.Common; // old + nadekobot specific things
global using NadekoBot.Common.Attributes;
global using NadekoBot.Extensions;

// discord
global using Discord;
global using Discord.Commands;
global using Discord.Net;
global using Discord.WebSocket;

// aliases
global using GuildPerm = Discord.GuildPermission;
global using ChannelPerm = Discord.ChannelPermission;
global using BotPermAttribute = Discord.Commands.RequireBotPermissionAttribute;
global using LeftoverAttribute = Discord.Commands.RemainderAttribute;
global using TypeReaderResult = NadekoBot.Common.TypeReaders.TypeReaderResult;

// non-essential
global using JetBrains.Annotations;