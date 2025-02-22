# Setting up NadekoBot on Linux

| Table of Contents                                   |
| :-------------------------------------------------- |
| [Linux From Source]                                 |
| [Setting up Nadeko on a VPS (Digital Ocean)]        |

--8<-- "docs/creds-guide.md"

### Operating System Compatibility

- Ubuntu: 20.04, 22.04, 24.04
- Mint: 19, 20, 21
- Debian: 10, 11, 12
- RockyLinux: 8, 9
- AlmaLinux: 8, 9
- openSUSE Leap: 15.5, 15.6 & Tumbleweed
- Fedora: 38, 39, 40, 41, 42
- Arch, Artix

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

2. Download and run the **new** installer script
    ``` sh
        cd ~ && 
        curl -L -o n-install.sh https://gitlab.com/kwoth/nadeko-bash-installer/-/raw/v6/n-install.sh && 
        bash n-install.sh
    ```
3. Install the bot (type `1` and press enter)
4. Edit creds (type `3` and press enter)
    3.1 *ALTERNATIVELY* You can exit the installer (option `6`) and edit `nadeko/creds.yml` file yourself
5. [Click here to follow creds guide](../../creds-guide)
    - After you're done, you can close nano (and save the file) by inputting, in order
       - `CTRL` + `X`
       - `Y`
       - `Enter`
6. Run the installer script again
    - `bash n-install.sh`
7. Run the bot (type `3` and press enter)

##### Update Instructions

1. ⚠ Stop the bot ⚠
2. Update and run the **new** installer script `cd ~ && wget -N https://gitlab.com/kwoth/nadeko-bash-installer/-/raw/v5/n-install.sh && bash n-install.sh`
3. Update the bot (type `2` and press enter)
4. Run the bot (type `3` and press enter)
5. 🎉

## Running Nadeko

### Tmux Method (Preferred)

Using `tmux` is the simplest method, and is therefore recommended for most users.

**Before proceeding, make sure your bot is not running by either running `.die` in your Discord server or exiting the process with `Ctrl+C`.**

If you are presented with the installer main menu, exit it by choosing Option `8`.

1. Create a new session: `tmux new -s nadeko`

The above command will create a new session named **nadeko** *(you can replace “nadeko” with anything you prefer, it's your session name)*.

2. Run the installer: `bash n-install.sh`

3. There are a few options when it comes to running Nadeko.

    - Run `3` to *Run the bot normally*
    - Run `4` to *Run the bot with Auto Restart* (This is may or may not work)

4. If option `4` was selected, you have the following options
```
1. Run Auto Restart normally without updating NadekoBot.
2. Run Auto Restart and update NadekoBot.
3. Exit

Choose:
[1] to Run NadekoBot with Auto Restart on "die" command without updating.
[2] to Run with Auto Updating on restart after using "die" command.
```
- Run `1` to restart the bot without updating. (This is done using the `.die` command)
- Run `2` to update the bot upon restart. (This is also done using the `.die` command)

5. That's it! to detatch the tmux session:
    - Press `Ctrl` + `B`
    - Then press `D`

Now check your Discord server, the bot should be online. Nadeko should now be running in the background of your system.

To re-open the tmux session to either update, restart, or whatever, execute `tmux a -t nadeko`. *(Make sure to replace "nadeko" with your session name. If you didn't change it, leave it as it is.)*


### Systemd

Compared to using tmux, this method requires a little bit more work to set up, but has the benefit of allowing Nadeko to automatically start back up after a system reboot or the execution of the `.die` command.

1. Navigate to the project's root directory
    - Project root directory location example: `/home/user/nadekobot/`
2. Use the following command to create a service that will be used to start Nadeko:

    ```bash
    echo "[Unit]
    Description=NadekoBot service
    After=network.target
    StartLimitIntervalSec=60
    StartLimitBurst=2

    [Service]
    Type=simple
    User=$USER
    WorkingDirectory=$PWD/output
    # If you want Nadeko to be compiled prior to every startup, uncomment the lines
    # below. Note  that it's not neccessary unless you are personally modifying the
    # source code.
    #ExecStartPre=/usr/bin/dotnet build ../src/NadekoBot/NadekoBot.csproj -c Release -o output/
    ExecStart=/usr/bin/dotnet NadekoBot.dll
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
4. Start Nadeko:
    - `sudo systemctl start nadeko.service && sudo systemctl enable nadeko.service`


### Systemd + Script

This method is similar to the one above, but requires one extra step, with the added benefit of better error logging and control over what happens before and after the startup of Nadeko.

1. Locate the project and move to its parent directory
    - Project location example: `/home/user/nadekobot/`
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
    yt-dlp -U

    # If you want Nadeko to be compiled prior to every startup, uncomment the lines
    # below. Note  that it's not necessary unless you are personally modifying the
    # source code.
    #echo \"Compiling NadekoBot...\"
    #cd \"$PWD\"/nadekobot
    #dotnet build src/NadekoBot/NadekoBot.csproj -c Release -o output/

    echo \"Starting NadekoBot...\"

    while true; do
        if [[ -d $PWD/nadekobot/output ]]; then
            cd $PWD/nadekobot/output || {
                echo \"Failed to change working directory to $PWD/nadekobot/output\" >&2
                echo \"Ensure that the working directory inside of '/etc/systemd/system/nadeko.service' is correct\"
                echo \"Exiting...\"
                exit 1
            }
        else
            echo \"$PWD/nadekobot/output doesn't exist\"
            exit 1
        fi

        dotnet NadekoBot.dll || {
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

### Setting up Nadeko on a Linux VPS (Digital Ocean Droplet)

If you want Nadeko to play music for you 24/7 without having to hosting it on your PC and want to keep it cheap, reliable and convenient as possible, you can try Nadeko on Linux Digital Ocean Droplet using the link [DigitalOcean](http://m.do.co/c/46b4d3d44795/) (by using this link, you will get **$10 credit** and also support Nadeko)

To set up the VPS, please select the options below
```
These are the min requirements you must follow:

OS: Any between Ubuntu, Fedora, and Debian

Plan: Basic

CPU options: regular with SSD
1 GB / 1 CPU
25 GB SSD Disk
1000 GB transfer

Note: You can select the cheapest option with 512 MB /1 CPU but this has been a hit or miss.

Datacenter region: Choose one depending on where you are located.

Authentication: Password or SSH
(Select SSH if you know what you are doing, otherwise choose password)
```
**Setting up NadekoBot**
Assuming you have followed the link above to setup an account and a Droplet with a 64-bit operational system on Digital Ocean and got the `IP address and root password (in your e-mail)` to login, it's time to get started.

**This section is only relevant to those who want to host Nadeko on DigitalOcean. Go through this whole section before setting the bot up.**

#### Prerequisites

- Download [PuTTY](http://www.chiark.greenend.org.uk/~sgtatham/putty/download.html)
- Download [WinSCP](https://winscp.net/eng/download.php) *(optional)*
- [Create and invite the bot](../../creds-guide).

#### Starting up

- **Open PuTTY** and paste or enter your `IP address` and then click **Open**.
  If you entered your Droplets IP address correctly, it should show **login as:** in a newly opened window.
- Now for **login as:**, type `root` and press enter.
- It should then ask for a password. Type the `root password` you have received in your e-mail address, then press Enter.

If you are running your droplet for the first time, it will most likely ask you to change your root password. To do that, copy the **password you've received by e-mail** and paste it on PuTTY.

- To paste, just right-click the window (it won't show any changes on the screen), then press Enter.
- Type a **new password** somewhere, copy and paste it on PuTTY. Press Enter then paste it again.

**Save the new password somewhere safe.**

After that, your droplet should be ready for use. [Follow the guide from the beginning](#linux-from-source) to set Nadeko up on your newly created VPS.

[Linux From Source]: #linux-from-source
[Source Update Instructions]: #source-update-instructions
[Linux Release]: #linux-release
[Release Update Instructions]: #release-update-instructions
[Tmux (Preferred Method)]: #tmux-preferred-method
[Systemd]: #systemd
[Systemd + Script]: #systemd-script
[Setting up Nadeko on a VPS (Digital Ocean)]: #setting-up-nadeko-on-a-linux-vps-digital-ocean-droplet
