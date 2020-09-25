# Get .NET Core v3.1 build image
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build

# install cron.
RUN apt-get update
RUN apt-get install -y cron
ADD docker-crontab /
RUN crontab /docker-crontab

# copy api assemblies and files
RUN mkdir /var/app
RUN mkdir /var/app/logs
COPY ./bin/release/netcoreapp3.1/publish /var/app
COPY appsettings.json /var/app/appsettings.json

# couple data folder data volume.
VOLUME ["/var/app/data"]
VOLUME ["/var/app/logs"]

# Set the default command to execute when creating a new container
WORKDIR /var/app/
# Note: we start cron in foreground mode, so it doesn't do something right away
ENTRYPOINT cron -f