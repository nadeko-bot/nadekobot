# Setting up NadekoBot on Linux

### Operating System Compatibility

- Ubuntu: 20.04, 22.04, 24.04
- Mint: 19, 20, 21
- Debian: 10, 11, 12
- RockyLinux: 8, 9
- AlmaLinux: 8, 9
- openSUSE Leap: 15.5, 15.6 & Tumbleweed
- Fedora: 38, 39, 40, 41, 42
- Arch, Artix
- MacOS: 11+ ?

--8<-- "md/creds-guide.md"

--8<-- "md/guides/vps-linux-guide.md"

### Installation Instructions

Open Terminal (if you're on an installation with a window manager) and navigate to the location where you want to install the bot (for example `cd ~`)

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
    /// tab | MacOS

    ```bash
    brew install curl
    ```

    ///


2. Download and run the **new** installer script
    ``` sh
        cd ~ &&
        curl -L -o n-install.sh https://raw.githubusercontent.com/nadeko-bot/bash-installer/refs/heads/v6/n-install.sh &&
        bash n-install.sh
    ```
3. Install the bot (type `1` and press enter)
4. Edit creds (type `3` and press enter)
    3.1 *ALTERNATIVELY* You can exit the installer (option `6`) and edit `nadeko/creds.yml` file yourself
5. [Click here to follow creds guide](../creds-guide.md)
    - After you're done, you can close nano (and save the file) by inputting, in order
       - `CTRL` + `X`
       - `Y`
       - `Enter`
6. Run the installer script again
    - `bash n-install.sh`
7. Run the bot (type `3` and press enter)
8. Done!

#### Update Instructions

1. ⚠ Stop the bot ⚠
2. Navigate to your bot's folder, we'll use home directory as an example
    - `cd ~`
3. Simply re-install the bot with a newer version by running the installer script
    - `curl -L -o n-install.sh https://raw.githubusercontent.com/nadeko-bot/bash-installer/refs/heads/v6/n-install.sh && bash n-install.sh`
4. Select option 1, and select a NEWER version

## Running Nadeko

### Tmux Method (Preferred)

Using `tmux` is the simplest method, and is therefore recommended for most users.

**Before proceeding, make sure your bot is not running by either running `.die` in your Discord server or exiting the process with `Ctrl+C`.**

If you are presented with the installer main menu, exit it by choosing Option `8`.

1. Create a new session: `tmux new -s nadeko`

The above command will create a new session named **nadeko** *(you can replace “nadeko” with anything you prefer, it's your session name)*.

1. Run the installer: `bash n-install.sh`

1. There are a few options when it comes to running Nadeko.

    - Type `2` to *Run the bot*

1. That's it! to detach the tmux session:
    - Press `Ctrl` + `B`
    - Then press `D`

Now check your Discord server, the bot should be online. Nadeko should now be running in the background of your system.

To re-open the tmux session to either update, restart, or whatever, execute `tmux a -t nadeko`. *(Make sure to replace "nadeko" with your session name. If you didn't change it, leave it as it is.)*

### Systemd + Script

This method is similar to the one above, but requires one extra step, with the added benefit of better error logging and control over what happens before and after the startup of Nadeko.

1. Locate your nadeko folder
    - Nadeko location example: `/home/user/nadeko/`
    - Parent directory example: `/home/user/`
2. Use the following command to create a service that will be used to execute `NadekoRun.sh`:

    ```bash
    echo "[Unit]
    Description=NadekoBot service
    After=network.target
    StartLimitIntervalSec=60
    StartLimitBurst=2

    [Service]
    Type=simple
    User=$USER
    WorkingDirectory=$_WORKING_DIR
    ExecStart=/bin/bash NadekoRun.sh
    Restart=on-failure
    RestartSec=5
    StandardOutput=syslog
    StandardError=syslog
    SyslogIdentifier=NadekoBot

    [Install]
    WantedBy=multi-user.target" | sudo tee /etc/systemd/system/nadeko.service
    ```

3. Make the new service available:
    - `sudo systemctl daemon-reload`
4. Use the following command to create a script that will be used to start Nadeko:

    ```bash
    {
    echo '#!/bin/bash'
    echo ""
    echo "echo \"Running NadekoBot in the background with auto restart\"

    while true; do
        if [[ -d $PWD/nadeko ]]; then
            cd $PWD/nadeko || {
                echo \"Failed to change working directory to $PWD/nadeko\" >&2
                echo \"Ensure that the working directory inside of '/etc/systemd/system/nadeko.service' is correct\"
                echo \"Exiting...\"
                exit 1
            }
        else
            echo \"$PWD/nadeko doesn't exist\"
            exit 1
        fi

        ./NadekoBot || {
            echo \"An error occurred when trying to start NadekBot\"
            echo \"Exiting...\"
            exit 1
        }

        echo \"Waiting for 5 seconds...\"
        sleep 5
        yt-dlp -U
        echo \"Restarting NadekoBot...\"
    done

    echo \"Stopping NadekoBot...\""
    } > NadekoRun.sh
    ```

5. Start Nadeko:
    - `sudo systemctl start nadeko.service && sudo systemctl enable nadeko.service`

[Source Update Instructions]: #source-update-instructions
[Release Update Instructions]: #release-update-instructions
[Tmux (Preferred Method)]: #tmux-preferred-method
