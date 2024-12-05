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
    private string _stateFilePath = "video_state.txt";  // File to store the current ID
    private string _discordToken;
    private string _youtubeCsv;
    private string _guildId;
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
        _client.InteractionCreated += InteractionCreatedAsync;
        
        _stateFilePath = Environment.GetEnvironmentVariable("STATE_FILE_PATH") ?? throw new InvalidOperationException();
        _discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? throw new InvalidOperationException();
        _youtubeCsv = Environment.GetEnvironmentVariable("YOUTUBE_CSV") ?? throw new InvalidOperationException();
        _guildId = Environment.GetEnvironmentVariable("GUILD_ID") ?? throw new InvalidOperationException();

        // Load saved video ID state
        LoadState();

        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        LoadVideoData();

        await Task.Delay(-1);  // Keep the bot running
    }

    private async Task SaveStateAsync()
    {
        Console.WriteLine("Saving state...");
        await File.WriteAllTextAsync(_stateFilePath, _currentId.ToString());
    }

    private void LoadState()
    {
        if (File.Exists(_stateFilePath))
        {
            var content = File.ReadAllText(_stateFilePath);
            if (int.TryParse(content, out int savedId))
            {
                Console.WriteLine("Loading state...");
                _currentId = savedId;
            }
        }
    }
    
    private void LoadVideoData()
    {
        try
        {
            using var reader = new StreamReader(_youtubeCsv);
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
        var guild = _client.GetGuild(Convert.ToUInt64(_guildId)); // Replace with your guild ID
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
            if (command.User.Id != 154537457008902144)  // Replace with your specific user ID
            {
                return;
            }

            var originalUrl = _videoData[_currentId];
            var modifiedUrl = originalUrl.Replace("www.youtube.com", "inv.nadeko.net");

            var components = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            // Add newline before the link
            var messageContent = $"**Video {_currentId}:**\n{modifiedUrl}";

            await command.RespondAsync(messageContent, components: components, ephemeral:true);
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
        string messageContent;

        if (component.Data.CustomId == "video_back" && _currentId > 1)
        {
            _currentId--;
        }
        else if (component.Data.CustomId == "video_next" && _currentId < _videoData.Count)
        {
            _currentId++;
        }
        else if (component.Data.CustomId == "video_bookmarked")
        {
            await component.RespondAsync("Ya esta guardado", ephemeral: true);
            Console.WriteLine("Video bookmarked");
        }
        else if (component.Data.CustomId == "video_bookmark")
        {
            // When the user clicks "Bookmark"
            var bookmarkUrl = _videoData[_currentId];  // Renamed variable to avoid conflict
            var modifiedUrl = bookmarkUrl.Replace("www.youtube.com", "inv.nadeko.net");

            // Save the current state to persist the video ID
            await SaveStateAsync();

            // Remove the current navigation buttons and send a new message with the "Bookmarked" button
            messageContent = $"**Video {_currentId}:**\n{modifiedUrl}";
            
            // Assuming `modifiedUrl` is the original YouTube URL with "www.youtube.com"
            var originalYoutubeUrl = modifiedUrl.Replace("inv.nadeko.net", "www.youtube.com");

            // Create the components with a disabled "⭐ Guardado" button and YouTube link button
            var bookmarkComponents = new ComponentBuilder()
                .WithButton("⭐ Guardado", "video_bookmarked", ButtonStyle.Secondary)
                .WithButton("🎥 YouTube", null, ButtonStyle.Link, url: originalYoutubeUrl)
                .Build();

            // Update the current message without the navigation buttons
            await component.UpdateAsync(msg =>
            {
                msg.Content = messageContent;
                msg.Embed = null;  // No embed for the preview
                msg.Components = bookmarkComponents;  // Replace with bookmark and YouTube link button
            });
            
            // Send a duplicated message with the "Back", "Next", and "Bookmark" buttons
            var navigationComponents = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            await component.RespondAsync(messageContent, components: navigationComponents, ephemeral:true);
            return;
        }
        
            // Default case for handling back, next, or other button presses
            var defaultUrl = _videoData[_currentId];  // Renamed variable to avoid conflict
            var modifiedUrlDefault = defaultUrl.Replace("www.youtube.com", "inv.nadeko.net");

            // Save the current state to persist the video ID
            await SaveStateAsync();

            // Add newline before the link
            messageContent = $"**Video {_currentId}:**\n{modifiedUrlDefault}";

            // Send the updated message with the navigation buttons
            var navigationButtons = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            await component.UpdateAsync(msg =>
            {
                msg.Content = messageContent;
                msg.Embed = null;
                msg.Components = navigationButtons;  // Re-add the navigation buttons
            });
        }
}
