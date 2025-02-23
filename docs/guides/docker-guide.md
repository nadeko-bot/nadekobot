# Deploying NadekoBot with Docker: A Comprehensive Guide

--8<-- "docs/creds-guide.md"

## Install NadekoBot with Docker

Ensure Docker is installed. If not, follow the official Docker guides for your specific operating system:  
  - [Docker Installation Guide](https://docs.docker.com/engine/install/)

1. Move to a directory where you want your Nadekobot's data folder to be (data folder will keep the database and config files) and create a data folder there.  
  ``` sh
    cd ~ && mkdir nadeko && cd nadeko && mkdir data
  ```  
1. Mount the newly created empty data folder as a volume while starting your docker container. Replace YOUR_TOKEN_HERE with the bot token obtained from the creds guide above.  
  ``` sh
  docker run -d --name nadeko ghcr.io/nadeko-bot/nadekobot:v6 -e bot_token=YOUR_TOKEN_HERE -v "./data:/app/data" && docker logs -f --tail 500 nadeko
  ```  
1. Enjoy 🎉

#### Updating your bot

If you want to update nadekobot to the latest version, all you have to do is pull the latest image and re-run.

1. Pull the latest image
  ``` sh
    docker pull ghcr.io/nadeko-bot/nadekobot:v6
  ```

1. Re-run your bot the same way you did before
  ``` sh
    docker run -d --name nadeko ghcr.io/nadeko-bot/nadekobot:v6 -e bot_token=YOUR_TOKEN_HERE -v "./data:/app/data" && docker logs -f --tail 500 nadeko
  ```  
1. Done! 🎉

## Install NadekoBot with Docker Compose

Ensure Docker Compose is installed on your system. If not, follow the official Docker guides for your specific operating system:  

  - [Docker Compose Installation Guide](https://docs.docker.com/compose/install/)

## Step-by-Step Installation

1. **Choose Your Workspace:** Select a directory where you'll set up your NadekoBot stack. Use your terminal to navigate to this directory. For the purpose of this guide, we'll use `/opt/stacks/nadekobot/` as an example, but you can choose any directory that suits your needs.

2. **Create a Docker Compose File:** In this directory, create a Docker Compose file named `docker-compose.yml`. You can use any text editor for this task. For instance, to use the `nano` editor, type `nano docker-compose.yml`.

3. **Configure Your Docker Compose File:** Populate your Docker Compose file with the following configuration:
  ``` yml
    services:
      nadeko:
        image: ghcr.io/nadeko-bot/nadekobot:v6
        container_name: nadeko
        restart: unless-stopped
        environment:
          TZ: Europe/Rome
          bot_token: YOUR_TOKEN_HERE
        volumes:
          - /opt/stacks/nadekobot/data:/app/data
    networks: {}
  ```

4. **Launch Your Bot:** Now, you're ready to run Docker Compose. Use the following command: `docker-compose up -d`.

## Keeping Your Bot Up-to-Date

1. **Navigate to Your Directory:** Use `cd /path/to/your/directory` to go to the directory containing your Docker Compose file.

2. **Pull the Latest Images:** Use `docker-compose pull` to fetch the latest images.

3. **Restart Your Containers:** Use `docker-compose up -d` to restart the containers.