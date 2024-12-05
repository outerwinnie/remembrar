using Discord;
using Discord.WebSocket;
using CsvHelper;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

class Program
{
    private readonly DiscordSocketClient _client;
    private Dictionary<int, string> _videoData;
    private int _currentId = 1;  // Start at ID 1

    public static Task Main(string[] args) => new Program().MainAsync();

    public Program()
    {
        _client = new DiscordSocketClient();
    }

    public async Task MainAsync()
    {
        _client.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.SlashCommandExecuted += SlashCommandExecutedAsync;
        _client.InteractionCreated += InteractionCreatedAsync; // Handle button interactions

        await _client.LoginAsync(TokenType.Bot, "xxx"); // Replace with your bot token
        await _client.StartAsync();

        // Load video data from CSV file
        LoadVideoData();

        await Task.Delay(-1); // Keep the bot running
    }

    private void LoadVideoData()
    {
        try
        {
            using var reader = new StreamReader("youtube_videos.csv");
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

            var records = csv.GetRecords<dynamic>();
            _videoData = records
                .Select((record, index) => new { Id = index + 1, Url = (string)record.URL })
                .ToDictionary(x => x.Id, x => x.Url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading video data: {ex.Message}");
        }
    }


    private async Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
    }

    private async Task ReadyAsync()
    {
        var guild = _client.GetGuild(757271564227182602); // Replace with your guild ID
        var command = new SlashCommandBuilder()
            .WithName("inicializar")
            .WithDescription("Inicializa el bot con el primer video");

        await guild.CreateApplicationCommandAsync(command.Build());
        Console.WriteLine("Bot is ready.");
    }

    private async Task SlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (command.CommandName == "inicializar")
        {
            // Ensure only a specific user can run this command
            if (command.User.Id != 154537457008902144)  // Replace with the specific user ID
            {
                await command.RespondAsync("You do not have permission to use this command.", ephemeral: true);
                return;
            }

            var videoUrl = _videoData[_currentId];
            var embed = new EmbedBuilder()
                .WithTitle($"Video ID: {_currentId}")
                .WithUrl(videoUrl)
                .WithDescription(videoUrl)
                .Build();

            var components = new ComponentBuilder()
                .WithButton("⬅️ Back", "video_back", ButtonStyle.Primary)
                .WithButton("Next ➡️", "video_next", ButtonStyle.Primary)
                .Build();

            await command.RespondAsync(embed: embed, components: components);
        }
    }

    private async Task InteractionCreatedAsync(SocketInteraction interaction)
    {
        if (interaction is SocketMessageComponent component)
        {
            await ButtonExecuted(component);
        }
    }

    private async Task ButtonExecuted(SocketMessageComponent component)
    {
        bool updated = false;

        if (component.Data.CustomId == "video_back" && _currentId > 1)
        {
            _currentId--;
            updated = true;
        }
        else if (component.Data.CustomId == "video_next" && _currentId < _videoData.Count)
        {
            _currentId++;
            updated = true;
        }

        if (updated)
        {
            var videoUrl = _videoData[_currentId];
            var embed = new EmbedBuilder()
                .WithTitle($"Video ID: {_currentId}")
                .WithDescription("Use the buttons below to navigate through videos.")
                .Build();

            await component.UpdateAsync(msg =>
            {
                msg.Embed = embed;
                msg.Content = videoUrl; // Display plain URL for preview
            });
        }
        else
        {
            await component.RespondAsync("No more videos in this direction.", ephemeral: true);
        }
    }
}
