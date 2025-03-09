# Docker Guide

### Prerequisites

- [Docker Core Engine](https://docs.docker.com/engine/install/)
- [Docker Compose](https://docs.docker.com/compose/install/) (optional, but recommended)

---

??? note "Creating a Discord Bot & Getting Credentials"
    --8<-- "md/creds-guide.md"

---

## Installing NadekoBot with Docker

When deploying NadekoBot with Docker, you have two options: using [Docker](#__tabbed_1_1) or [Docker Compose](#__tabbed_1_2). The following sections provide step-by-step instructions for both methods.

/// tab | Docker

### Deploying NadekoBot with Docker

1. Move to a directory where you want your Nadekobot's data folder to be (data folder will keep the database and config files) and create a data folder there.
    ``` sh
    cd ~ && mkdir nadeko && cd nadeko && mkdir data
    ```
2. Mount the newly created empty data folder as a volume while starting your docker container. Replace YOUR_TOKEN_HERE with the bot token obtained from the creds guide above.
    ``` sh
    docker run -d --name nadeko ghcr.io/nadeko-bot/nadekobot:v6 -e bot_token=YOUR_TOKEN_HERE -v "./data:/app/data" && docker logs -f --tail 500 nadeko
    ```
3. Enjoy! 🎉

### Updating your bot

If you want to update nadekobot to the latest version, all you have to do is pull the latest image and re-run.

1. Pull the latest image
    ``` sh
    docker pull ghcr.io/nadeko-bot/nadekobot:v6
    ```
2. Re-run your bot the same way you did before
    ``` sh
    docker run -d --name nadeko ghcr.io/nadeko-bot/nadekobot:v6 -e bot_token=YOUR_TOKEN_HERE -v "./data:/app/data" && docker logs -f --tail 500 nadeko
    ```
3. Done! 🎉

///
/// tab | Docker Compose

1. **Choose Your Workspace:** Select a directory where you'll set up your NadekoBot stack. Use your terminal to navigate to this directory. For the purpose of this guide, we'll use `/opt/stacks/nadeko/` as an example, but you can choose any directory that suits your needs.
2. **Create a Docker Compose File:** In this directory, create a Docker Compose file named `docker-compose.yml`. You can use any text editor for this task. For instance, to use the `nano` editor, type `nano docker-compose.yml`.
3. **Configure Your Docker Compose File:** Populate your Docker Compose file with the following configuration:
    ``` yml
    services:
      nadeko:
        image: ghcr.io/nadeko-bot/nadekobot:v6
        container_name: nadeko
        restart: unless-stopped
        environment:
          TZ: Europe/Rome # Modify this to your timezone
          bot_token: YOUR_TOKEN_HERE
        volumes:
          - /opt/stacks/nadeko/data:/app/data
    networks: {}
    ```

1. **Launch Your Bot:** Now, you're ready to run Docker Compose. Use the following command: `docker compose up -d`.
2. **Navigate to Your Directory:** Use `cd /opt/stacks/nadeko/` to go to the directory containing your Docker Compose file.
3. **Pull the Latest Images:** Use `docker compose pull` to fetch the latest images.
4. **Restart Your Containers:** Use `docker compose up -d` to restart the containers.

///
