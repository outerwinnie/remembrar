# Use the official .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

# Set the working directory in the container
WORKDIR /app

# Copy the project files to the container
COPY *.csproj ./

# Restore the dependencies for the project
RUN dotnet restore

# Copy the remaining source code to the container
COPY . .

# Build the application in Release mode
RUN dotnet publish -c Release -o /app/out

# Use the runtime-only image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# Set the working directory in the container
WORKDIR /app

# Copy the built application from the build stage
COPY --from=build /app/out .

# Ensure that the bot can read and write the video state file
VOLUME ["/app/video_state"]

# Environment variable for the bot token (to be provided during container run)
ENV DISCORD_BOT_TOKEN=""
ENV YOUTUBE_CSV="youtube_videos.csv"
ENV GUILD_ID=""
ENV VIDEO_PROGRESS="video_progress.csv"
ENV BOOKMARKED_VIDEOS="bookmarked_videos.csv"
ENV THREAD_ID=""

# Expose port if necessary (optional for Discord bots)
# EXPOSE 80

# Run the bot
CMD ["dotnet", "Remembrar.dll"]
