There are two main methods to run NadekoBot on Linux: using `tmux` or using `systemd` with a script.

/// tab | Tmux (Preferred Method)

--8<-- [start:macos]
Using `tmux` is the simplest method, and is therefore recommended for most users.

!!! warning
    Before proceeding, make sure your bot is not currently running by either running `.die` in your Discord server or exiting the process with `Ctrl+C`.

1. Access the directory where `n-install.sh` and `nadeko` is located.
2. Create a new tmux session: `tmux new -s nadeko`
    - The above command will create a new session named **nadeko**. You may replace **nadeko** with any name you prefer.
3. Run the installer: `bash n-install.sh`
4. Start the bot by typing `3` and pressing `Enter`.
5. Detach from the tmux session, allowing the bot to run in the background:
    - Press `Ctrl` + `B`
    - Then press `D`

Now check your Discord server, the bot should be online. Nadeko should now be running in the background of your system.

To re-open the tmux session to either update, restart, or whatever, execute `tmux a -t nadeko`. *(Make sure to replace "nadeko" with your session name. If you didn't change it, leave it as it is.)*
--8<-- [end:macos]

///
/// tab | Systemd

This method is a bit more complex and involved, but comes with the added benefit of better error logging and control over what happens before and after the startup of Nadeko.

1. Access the directory where `n-install.sh` and `nadeko` is located.
2. Use the following command to create a service that will be used to execute `NadekoRun.bash`:
    ```bash
    echo "[Unit]
    Description=NadekoBot service
    After=network.target
    StartLimitIntervalSec=60
    StartLimitBurst=2

    [Service]
    Type=simple
    User=$USER
    WorkingDirectory=$PWD
    ExecStart=/bin/bash NadekoRun.bash
    #ExecStart=./nadeko/NadekoBot
    Restart=on-failure
    RestartSec=5
    StandardOutput=journal
    StandardError=journal
    SyslogIdentifier=NadekoBot

    [Install]
    WantedBy=multi-user.target" | sudo tee /etc/systemd/system/nadeko.service
    ```
3. Make the new service available: `sudo systemctl daemon-reload`
4. Use the following command to create a script that will be used to start Nadeko:
    ```bash
    cat <<EOF > NadekoRun.bash
    #!/bin/bash

    export PATH="$HOME/.local/bin:$PATH"

    is_python3_installed=\$(command -v python3 &>/dev/null && echo true || echo false)
    is_yt_dlp_installed=\$(command -v yt-dlp &>/dev/null && echo true || echo false)

    [[ \$is_python3_installed == true ]] \\
        && echo "[INFO] python3 path: \$(which python3)" \\
        && echo "[INFO] python3 version: \$(python3 --version)"
    [[ \$is_yt_dlp_installed == true ]] \\
        && echo "[INFO] yt-dlp path: \$(which yt-dlp)"

    echo "[INFO] Running NadekoBot in the background with auto restart"
    if [[ \$is_yt_dlp_installed == true ]]; then
        yt-dlp -U || echo "[ERROR] Failed to update 'yt-dlp'" >&2
    fi

    echo "[INFO] Starting NadekoBot..."

    while true; do
        if [[ -d $PWD/nadeko ]]; then
            cd "$PWD/nadeko" || {
                echo "[ERROR] Failed to change working directory to '$PWD/nadeko'" >&2
                echo "[INFO] Exiting..."
                exit 1
            }
        else
            echo "[WARN] '$PWD/nadeko' doesn't exist" >&2
            echo "[INFO] Exiting..."
            exit 1
        fi

        ./NadekoBot || {
            echo "[ERROR] An error occurred when trying to start NadekoBot" >&2
            echo "[INFO] Exiting..."
            exit 1
        }

        echo "[INFO] Waiting 5 seconds..."
        sleep 5
        if [[ \$is_yt_dlp_installed == true ]]; then
            yt-dlp -U || echo "[ERROR] Failed to update 'yt-dlp'" >&2
        fi
        echo "[INFO] Restarting NadekoBot..."
    done

    echo "[INFO] Stopping NadekoBot..."
    EOF
    ```

With everything set up, you can run NadekoBot in one of three modes:

1. **Auto-Restart Mode**: NadekoBot will restart automatically if you restart it via the `.die` command.
    - To enable this mode, start the service: `sudo systemctl start nadeko`
2. **Auto-Restart on Reboot Mode**: In addition to auto-restarting after `.die`, NadekoBot will also start automatically on system reboot.
    - To enable this mode, run:
        ```bash
        sudo systemctl enable nadeko
        sudo systemctl start nadeko
        ```
3. **Standard Mode**: NadekoBot will stop completely when you use `.die`, without restarting automatically.
    - To switch to this mode:
        1. Stop the service: `sudo systemctl stop nadeko`
        2. Edit the service file: `sudo <editor> /etc/systemd/system/nadeko.service`
        3. Modify the `ExecStart` line:
            - **Comment out**: `ExecStart=/bin/bash NadekoRun.bash`
            - **Uncomment**: `#ExecStart=./nadeko/NadekoBot`
        4. Save and exit the editor.
        5. Reload systemd: `sudo systemctl daemon-reload`
        6. Disable automatic startup: `sudo systemctl disable nadeko`
        7. Start NadekoBot manually: `sudo systemctl start nadeko`

///
