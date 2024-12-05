using Discord;
using Discord.WebSocket;
using CsvHelper;
using System.Globalization;

class Program
{
    private readonly DiscordSocketClient _client;
    private Dictionary<int, string> _videoData;
    private string _discordToken;
    private string _youtubeCsv;
    private string _guildId;
    private string _videoProgress;
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
        
        _discordToken = Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN") ?? throw new InvalidOperationException();
        _youtubeCsv = Environment.GetEnvironmentVariable("YOUTUBE_CSV") ?? throw new InvalidOperationException();
        _guildId = Environment.GetEnvironmentVariable("GUILD_ID") ?? throw new InvalidOperationException();
        _videoProgress = Environment.GetEnvironmentVariable("VIDEO_PROGRESS") ?? throw new InvalidOperationException();

        // Load video data from CSV
        LoadVideoData();

        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        await Task.Delay(-1);  // Keep the bot running
    }

    private async Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log);
    }

    private async Task ReadyAsync()
    {
        var guild = _client.GetGuild(Convert.ToUInt64(_guildId));
        var command = new SlashCommandBuilder()
            .WithName("encender")
            .WithDescription("Enciende la televisión");

        await guild.CreateApplicationCommandAsync(command.Build());
        Console.WriteLine("Bot is ready.");
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

    private void LoadState(ulong userId)
    {
        try
        {
            // Ensure the file exists, if not, create it
            if (!File.Exists(_videoProgress))
            {
                using (var writer = new StreamWriter(_videoProgress))
                {
                    var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
                    csvWriter.WriteRecords(new List<UserProgress>());  // Create empty file
                }
            }

            using var reader = new StreamReader(_videoProgress);
            using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var records = csv.GetRecords<UserProgress>().ToList();

            var userProgress = records.FirstOrDefault(r => r.UserId == userId);
            if (userProgress != null)
            {
                _currentId = userProgress.CurrentId;
            }
            else
            {
                _currentId = 1;  // Default to video ID 1 if no record is found
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading state: {ex.Message}");
            _currentId = 1;  // Default to video ID 1 if an error occurs
        }
    }

    private async Task SaveStateAsync(ulong userId)
    {
        try
        {
            var userProgressList = new List<UserProgress>();

            // Check if the file exists, if not, create it
            if (!File.Exists(_videoProgress))
            {
                using (var writer = new StreamWriter(_videoProgress))
                {
                    var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
                    csvWriter.WriteRecords(new List<UserProgress>());  // Create empty file
                }
            }

            // Read existing records
            using (var reader = new StreamReader(_videoProgress))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                userProgressList = csv.GetRecords<UserProgress>().ToList();
            }

            // Update the user's progress
            var userProgress = userProgressList.FirstOrDefault(r => r.UserId == userId);
            if (userProgress != null)
            {
                userProgress.CurrentId = _currentId;
            }
            else
            {
                userProgressList.Add(new UserProgress { UserId = userId, CurrentId = _currentId });
            }

            // Save the updated list to the CSV file
            using (var writer = new StreamWriter(_videoProgress))
            using (var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csvWriter.WriteRecords(userProgressList);
            }

            Console.WriteLine("State saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving state: {ex.Message}");
        }
    }

    private async Task SlashCommandExecutedAsync(SocketSlashCommand command)
    {
        ulong userId = command.User.Id;  // Get the user ID
        LoadState(userId);  // Load the user's progress

        if (command.CommandName == "encender")
        {
            var originalUrl = _videoData[_currentId];
            var modifiedUrl = originalUrl.Replace("www.youtube.com", "inv.nadeko.net");

            var components = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            var messageContent = $"**Video {_currentId}:**\n{modifiedUrl}";

            await command.RespondAsync(messageContent, components: components, ephemeral: true);
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
        ulong userId = component.User.Id;  // Get the user ID
        string messageContent;

        if (component.Data.CustomId == "video_back" && _currentId > 1)
        {
            _currentId--;
        }
        else if (component.Data.CustomId == "video_next" && _currentId < _videoData.Count)
        {
            _currentId++;
        }

        // Save state after any button press
        await SaveStateAsync(userId);

        var defaultUrl = _videoData[_currentId];
        var modifiedUrlDefault = defaultUrl.Replace("www.youtube.com", "inv.nadeko.net");

        messageContent = $"**Video {_currentId}:**\n{modifiedUrlDefault}";

        var navigationButtons = new ComponentBuilder()
            .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
            .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
            .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
            .Build();

        await component.UpdateAsync(msg =>
        {
            msg.Content = messageContent;
            msg.Components = navigationButtons;
        });
    }
}

// Class to store user progress in the CSV
public class UserProgress
{
    public ulong UserId { get; set; }
    public int CurrentId { get; set; }
}
