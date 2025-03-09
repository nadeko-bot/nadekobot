1. First make sure that curl is installed

    /// tab | Ubuntu | Debian | Mint

    ```bash
    sudo apt install curl
    ```

    ///
    /// tab | Rocky | Alma | Fedora

    ```bash
    sudo dnf install curl
    ```

    ///
    /// tab | openSUSE

    ```bash
    sudo zypper install curl
    ```

    ///
    /// tab | Arch | Artix

    ```bash
    sudo pacman -S curl
    ```

    ///

--8<-- [start:macos]
1. Download and run the **new** installer script
    ``` sh
    cd ~
    curl -L -o n-install.sh https://raw.githubusercontent.com/nadeko-bot/bash-installer/refs/heads/v6/n-install.sh
    bash n-install.sh
    ```
2. Install the bot (type `1` and press enter)
3. Edit creds (type `3` and press enter)
    - *ALTERNATIVELY*, you can exit the installer (option `6`) and edit `nadeko/creds.yml` file yourself
5. Follow the instruction [below](#creating-your-own-discord-bot) to create your own Discord bot and obtain the credentials needed to run it.
    - After you're done, you can close nano (and save the file) by inputting, in order:
        - `CTRL` + `X`
        - `Y`
        - `Enter`
6. Run the installer script again
    - `bash n-install.sh`
7. Run the bot (type `3` and press enter)
8. Done!
--8<-- [end:macos]
