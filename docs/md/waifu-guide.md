---
title: NadekoBot waifu system - back, manage and feed waifus
description: How the NadekoBot waifu system works. Become a waifu, back one with your bank balance, buy the manager position and collect the payout of each cycle.
---

# Waifu system

The NadekoBot waifu system is an economy game. Members join as waifus. Fans back a waifu with their bank balance, and a manager buys the right to manage the waifu. At the end of each cycle, the bot pays the fans, the waifu and the manager.

Type `.waifuhelp` / `.whelp` in Discord for a short overview.

## Roles

| Role | How you get it | What you get |
|---|---|---|
| Waifu | `.waifuoptin` / `.woptin` (costs 10,000) | The waifu fee from each cycle |
| Fan | `.waifuback @user` / `.wback` | A share of the returns, based on your bank balance |
| Manager | `.waifubuy <amount> @user` / `.wbuy` | A cut of the waifu fee |

You can have all three roles at the same time. You do not have to be a waifu to be a fan or a manager. You cannot back or manage yourself.

The bot takes the opt-in cost, the bids and the gifts from your wallet. The payouts go to your wallet.

## How a cycle pays

A cycle is 24 hours. At the end of each cycle, the bot calculates the returns for each waifu which has a manager:

- **Backing**: the total bank balance of the fans, up to 1,000,000.
- **Rate**: 17% per year, divided over the cycles of the year.
- **Stats**: the average of mood and food. Full stats pay 100%. Empty stats pay nothing.

The bot then splits the returns:

1. The waifu fee (1% to 5% of the returns) goes to the waifu and the manager. The manager gets 15% of the fee, and the waifu gets the rest of it.
2. The fans share the rest of the returns in proportion to their bank balance.

The payouts add up until you claim them with `.waifupayout` / `.wp`.

A waifu without a manager earns nothing, and its fans earn nothing too.

## Be a waifu

1. Type `.waifuoptin` to join. It costs 10,000.
2. Type `.waifu` / `.w` to see your card. Type `.waifu @user` to see the card of another member.
3. To leave, type `.waifuoptout` / `.woptout`. You can leave only when you have no fans. Your manager then loses the position.

## Back a waifu

1. Put money in your bank with `.bank deposit <amount>`. Only your bank balance counts. Backing is free, and the money stays in your bank.
2. Type `.waifuback @user` to back a waifu. You can back only one waifu at a time.

The bot reads your bank balance when a cycle starts. A deposit counts from the next cycle.
3. To change to a different waifu, type `.waifuback @other`. The bot asks you to confirm.
4. To stop, type `.waifuback` without a user.

## Manage a waifu

1. Type `.waifubuy <amount> @user`. Your bid must be at least 15% more than the price of the waifu. Your bid becomes the new price.
2. The bot splits your bid:
    - The previous manager gets back up to the old price. If the waifu has no manager, nobody gets this part.
    - The waifu gets half of the rest.
    - The bot removes the other half from the economy.
3. The bot asks you to confirm the bid.
4. As the waifu, set your fee with `.waifufee <1-5>` / `.wfee`.

To see the waifus which you manage, type `.waifulist` / `.wlist`. To stop managing a waifu, type `.waifuresign @user` / `.wresign`. You get no refund, and the price of the waifu goes down to 1,000.

## Mood and food

Mood and food go from 0% to 100%. Both go down slowly over time. Keep them high to get a higher payout.

- **Free actions**: `.hug`, `.kiss` and `.pat` raise the mood of the waifu which you mention. `.nom` raises food. Each member gets 2 free actions per day (3 for patrons).
- **Gifts**: type `.waifugiftshop` / `.wgs` to see today's gifts. The shop changes its gifts every day at 00:00 UTC. Type `.waifugift <item> @user` / `.wg` to give a gift. To give more than one, put the count and `x` before the item, without spaces: `.waifugift 5xcookie @user`.

## Commands

| Command | What it does |
|---|---|
| `.waifu [@user]` | Shows a waifu card |
| `.waifuoptin` / `.waifuoptout` | Joins or leaves the waifu system |
| `.waifuback [@user]` | Backs a waifu, changes to a different waifu, or stops |
| `.waifubuy <amount> @user` | Buys the manager position |
| `.waifuresign @user` | Stops managing a waifu |
| `.waifufee <1-5>` | Sets your waifu fee |
| `.waifupayout` | Claims your payouts |
| `.waifugiftshop` | Shows the gifts of today |
| `.waifugift <item> @user` | Gives a gift |
| `.hug`, `.kiss`, `.pat`, `.nom` | Raise mood or food |
| `.waifulist [@user]` | Shows the waifus which a member manages |
| `.waifufans [@user]` | Shows the fans of a waifu |
| `.waifuleaderboard [price]` / `.wlb` | Shows the top waifus by backing, or by price |

You can see all [commands](https://nadeko.bot/commands/) on the NadekoBot site. In Discord, type `.h <command>` for details.

!!! note "Bot owners"
    The prices, rates, limits and gifts are in `data/waifu.yml`. The numbers on this page are the defaults. The fee range of 1% to 5% is fixed. To host your own bot, start with [Create a Discord bot](creds-guide.md).
