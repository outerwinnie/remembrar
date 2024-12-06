using Discord;
using Discord.WebSocket;
using CsvHelper;
using System.Globalization;

public class Bookmark
{
    public int VideoId { get; set; }
    public string Url { get; set; }
}

public class UserState
{
    public ulong UserId { get; set; }
    public int CurrentVideoId { get; set; }
}

class Program
{
    private readonly DiscordSocketClient _client;
    private Dictionary<int, string> _videoData;
    private string _stateFilePath;  // File to store the current ID
    private string _discordToken;
    private string _youtubeCsv;
    private string _guildId;
    private string _bookmarkedVideos;
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
        _bookmarkedVideos = Environment.GetEnvironmentVariable("BOOKMARKED_VIDEOS") ?? throw new InvalidOperationException();

        // Load saved video ID state
        //LoadState();

        await _client.LoginAsync(TokenType.Bot, _discordToken);
        await _client.StartAsync();

        LoadVideoData();

        await Task.Delay(-1);  // Keep the bot running
    }

    private void SaveUserState(ulong userId, int videoId)
    {
        Console.WriteLine($"Cheking user state for {userId}, to save...");
        List<UserState> userStates = LoadUserStates();
        var existingState = userStates.FirstOrDefault(s => s.UserId == userId);

        if (existingState != null)
        {
            existingState.CurrentVideoId = videoId;
        }
        else
        {
            userStates.Add(new UserState { UserId = userId, CurrentVideoId = videoId });
        }
        
        Console.WriteLine("Saving user state...");
        using var writer = new StreamWriter(_stateFilePath);
        using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
        csvWriter.WriteRecords(userStates);
    }

    private List<UserState> LoadUserStates()
    {
        if (!File.Exists(_stateFilePath)) return new List<UserState>();

        using var reader = new StreamReader(_stateFilePath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        Console.WriteLine("Loading user states...");
        return csv.GetRecords<UserState>().ToList();
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
            .WithName("encender")
            .WithDescription("Enciende la television");

        await guild.CreateApplicationCommandAsync(command.Build());
        Console.WriteLine("Bot is ready.");
    }

    private async Task SlashCommandExecutedAsync(SocketSlashCommand command)
    {
        if (command.CommandName == "encender")
        {
            var userId = command.User.Id;
            var userStates = LoadUserStates();
            var userState = userStates.FirstOrDefault(s => s.UserId == userId) ?? new UserState { UserId = userId, CurrentVideoId = 1 };
            
            var originalUrl = _videoData[userState.CurrentVideoId];
            var modifiedUrl = originalUrl.Replace("www.youtube.com", "inv.nadeko.net");

            var components = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            // Add newline before the link
            var messageContent = $"**Video {userState.CurrentVideoId}:**\n{modifiedUrl}";

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
        var userId = component.User.Id;
        var userStates = LoadUserStates();
        var userState = userStates.FirstOrDefault(s => s.UserId == userId) ?? new UserState { UserId = userId, CurrentVideoId = 1 };
        string messageContent;

        if (component.Data.CustomId == "video_back" && userState.CurrentVideoId > 1)
        {
            userState.CurrentVideoId--;
        }
        else if (component.Data.CustomId == "video_next" && userState.CurrentVideoId < _videoData.Count)
        {
            userState.CurrentVideoId++;
        }
        else if (component.Data.CustomId == "video_bookmarked")
        {
            await component.RespondAsync("Ya esta guardado", ephemeral: true);
            Console.WriteLine("Video bookmarked");
        }
        else if (component.Data.CustomId == "video_bookmark")
        {
            // When the user clicks "Bookmark"
            var bookmarkUrl = _videoData[userState.CurrentVideoId];  // Get the URL for the current video
            var modifiedUrl = bookmarkUrl.Replace("www.youtube.com", "inv.nadeko.net");

            // Save the current state to persist the video ID
            SaveUserState(userId, userState.CurrentVideoId);

            // Check if the video is already bookmarked
            var existingBookmarks = LoadBookmarks();
            if (existingBookmarks.Any(b => b.VideoId == userState.CurrentVideoId))
            {
                await component.RespondAsync("Este video ya está guardado.", ephemeral: true);
                return;  // Skip saving if the video is already bookmarked
            }

            // Save the bookmark to the CSV
            SaveBookmarkToCsv(userState.CurrentVideoId, modifiedUrl);

            // Create the components with a disabled "⭐ Guardado" button and YouTube link button
            var originalYoutubeUrl = modifiedUrl.Replace("inv.nadeko.net", "www.youtube.com");

            var bookmarkComponents = new ComponentBuilder()
                .WithButton("⭐ Guardado", "video_bookmarked", ButtonStyle.Secondary)
                .WithButton("🎥 YouTube", null, ButtonStyle.Link, url: originalYoutubeUrl)
                .Build();

            // Send the message with the bookmark components
            await component.Channel.SendMessageAsync($"**Video {userState.CurrentVideoId}:**\n{modifiedUrl}", components: bookmarkComponents);

            // Send a duplicated message with the "Back", "Next", and "Bookmark" buttons
            var navigationComponents = new ComponentBuilder()
                .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
                .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
                .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
                .Build();

            await component.RespondAsync($"**Video {userState.CurrentVideoId}:**\n{modifiedUrl}", components: navigationComponents, ephemeral: true);
            return;
        }

        // Default case for handling back, next, or other button presses
        var defaultUrl = _videoData[userState.CurrentVideoId];  // Get the default URL
        var modifiedUrlDefault = defaultUrl.Replace("www.youtube.com", "inv.nadeko.net");

        // Save the current state to persist the video ID
        SaveUserState(userId, userState.CurrentVideoId);

        // Send the updated message with the navigation buttons
        var navigationButtons = new ComponentBuilder()
            .WithButton("⬅️ Anterior", "video_back", ButtonStyle.Primary)
            .WithButton("Siguiente ➡️", "video_next", ButtonStyle.Primary)
            .WithButton("⭐ Guardar", "video_bookmark", ButtonStyle.Secondary)
            .Build();

        await component.UpdateAsync(msg =>
        {
            msg.Content = $"**Video {userState.CurrentVideoId}:**\n{modifiedUrlDefault}";
            msg.Components = navigationButtons;
        });
    }

    // Save the bookmark to CSV if it doesn't already exist
    private void SaveBookmarkToCsv(int videoId, string videoUrl)
    {
        try
        {
            List<Bookmark> bookmarks = LoadBookmarks();

            // Check if the video is already bookmarked
            if (bookmarks.Any(b => b.VideoId == videoId))
            {
                Console.WriteLine("Bookmark already exists.");
                return;  // Skip saving if it's a duplicate
            }

            // Add the new bookmark
            bookmarks.Add(new Bookmark { VideoId = videoId, Url = videoUrl });

            // Save the updated list to the CSV file
            using var writer = new StreamWriter(_bookmarkedVideos);
            using var csvWriter = new CsvWriter(writer, CultureInfo.InvariantCulture);
            csvWriter.WriteRecords(bookmarks);
            Console.WriteLine("Bookmark saved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving bookmark: {ex.Message}");
        }
    }

    // Load bookmarks from the CSV file
    private List<Bookmark> LoadBookmarks()
    {
        try
        {
            if (File.Exists(_bookmarkedVideos))
            {
                using var reader = new StreamReader(_bookmarkedVideos);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                return csv.GetRecords<Bookmark>().ToList();
            }
            else
            {
                return new List<Bookmark>();  // Return an empty list if no file exists
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading bookmarks: {ex.Message}");
            return new List<Bookmark>();  // Return an empty list if an error occurs
        }
    }
}
