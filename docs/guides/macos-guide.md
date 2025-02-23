--8<-- "docs/creds-guide.md"

### Homebrew/wget
You must have homebrew installed to run this guide.
*Skip this step if you already have homebrew installed*
- Copy and paste this command, then press Enter:
    - `/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"`

##### Installation Instructions

1. Download and run the **new** installer script 
    - `cd ~ && wget -N https://raw.githubusercontent.com/nadeko-bot/bash-installer/refs/heads/v6/n-install.sh && bash n-install.sh`
2. Install prerequisites (type `1` and press enter)
3. Download the bot (type `2` and press enter)
4. Exit the installer in order to set up your `creds.yml`
5. Copy the creds.yml template
    `cp nadekobot/output/creds_example.yml nadekobot/output/creds.yml`
6. Open `nadekobot/output/creds.yml` with your favorite text editor. We will use nano here
    - `nano nadekobot/output/creds.yml`
7. [Enter your bot's token](#creds-guide)
    - After you're done, you can close nano (and save the file) by inputting, in order
      - `CTRL`+`X`
      - `Y`
      - `Enter`
8. Run the bot (type `3` and press enter)

##### Update Instructions

1. ⚠ Stop the bot
2. Update and run the **new** installer script `cd ~ && wget -N https://raw.githubusercontent.com/nadeko-bot/bash-installer/refs/heads/v6/n-install.sh && bash n-install.sh`
3. Update the bot (type `2` and press enter)
4. Run the bot (type `3` and press enter)
5. 🎉