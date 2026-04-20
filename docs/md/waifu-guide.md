# Waifu System

The waifu system is an opt-in economy. You opt in to become a waifu, other users back you with their bank balance, and every cycle pays out based on your mood, food, and manager fee.

## Roles

- **Waifu** - opted-in user. Receives cycle payouts. Can be backed by fans and owned by a manager.
- **Fan** - a user backing a waifu. Their bank balance counts toward that waifu's returns.
- **Manager** - whoever bought the waifu's manager slot. Earns a cut of the waifu's fee each cycle.

A user can be all three at once: a waifu, a fan of someone else, and a manager of a third person.

## Becoming a waifu

*You don't have to be a waifu in order to be a manager or become a fan of another waifu*

1. Run `.waifuoptin` to join. Costs 10,000 by default (configurable as `OptInCost`).
2. Put money in your bank with `.deposit <amount>`. Only your bank balance counts as backing.
3. Run `.waifu` to see your own card, or `.waifu @user` to see someone else's.
4. Opt out anytime with `.waifuoptout`. You cannot opt out while you have fans.

## Backing a Waifu

Pick one waifu and back her with `.waifuback @user`. Your entire bank balance counts toward their returns while you back her. You can only back one waifu at a time.

Run `.waifuback` with no argument to stop. Switching targets is a single command: `.waifuback @newuser` prompts you to confirm the switch.

## Cycles and Payouts

A cycle is 24 hours by default (`CycleHours`). At the end of each cycle, every waifu earns payouts based on:

- Total backing from their fans (capped at `DefaultReturnsCap`, 1,000,000 by default)
- Annual base rate of 17% (`BaseReturnRate`), prorated per cycle
- Stat multiplier from `(mood + food) / 2`. Full stats pay 100%, empty stats pay 0%.
- The waifu's fee, split between the waifu and their manager

Claim your earnings with `.waifupayout`. Payouts accumulate until you claim them.

## Mood and Food

Waifus have two stats from 0 to 100%. Both decay over time and boost payouts when full.

**Free actions** (2 per user per day, set by `MaxDailyActions`):

- `.hug @user`, `.kiss @user`, `.pat @user` - raise mood
- `.nom @user` - raises food

**Gift shop** rotates 8 items daily. Run `.waifugiftshop` to see today's selection, then `.waifugift <item> @user` to send one. Prefix with a count to send multiples, for example `.waifugift 5xcookie @user`.

## Becoming a Manager

Buy the manager slot on any waifu with `.waifubuy <amount> @user`. Your bid must beat the current price by at least 15% (`ManagerBuyPremium`). The surplus splits: half to the waifu, the rest burned or refunded to the previous manager.

As manager:

- Set the waifu's fee with `.waifufee <1-5>`. Fee is a percent skimmed from cycle payouts.
- You keep 15% of that fee (`ManagerCutPercent`); the waifu keeps the rest.
- List your managed waifus with `.waifulist`.
- Step down with `.waifuresign @user`.

## Command Reference

| Command | Purpose |
|---------|---------|
| `.waifu [@user]` | Show a waifu card |
| `.waifuoptin` / `.waifuoptout` | Join or leave the system |
| `.waifuback [@user]` | Start, switch, or stop backing |
| `.waifubuy <amount> @user` | Buy manager slot |
| `.waifuresign @user` | Step down as manager |
| `.waifufee <1-5>` | Set your fee percent (waifu only) |
| `.waifupayout` | Claim pending payouts |
| `.waifugiftshop` | View today's gift rotation |
| `.waifugift <item> @user` | Send a gift |
| `.hug` `.kiss` `.pat` `.nom` | Raise mood or food |
| `.waifulist [@user]` | Waifus managed by a user |
| `.waifufans [@user]` | Fans backing a user |
| `.waifulb [price\|backing]` | Leaderboard |

Run `.h <command>` in Discord for full usage details.

!!! note "Bot owners"
    Every price, rate, and gift item lives in `data/waifu.yml`. Edit it to retune cycle length, return rate, opt-in cost, daily action limits, or the full gift shop catalog. Comments in the file document each field.