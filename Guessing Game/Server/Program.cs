using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

class Server
{
    private TcpListener tcpListener;
    private List<GameSession> gameSessions;
    private bool isRunning;
    private readonly string filesDirectory = @"C:\SET\Third_SEM\Windows_Programming\Guessing Game\Server";
    private object sessionLock = new object();

    public Server()
    {
        tcpListener = new TcpListener(IPAddress.Parse("127.0.0.1"), 8888);
        gameSessions = new List<GameSession>();
    }

    public void Start()
    {
        try
        {
            isRunning = true;
            tcpListener.Start();
            Console.WriteLine("Server started...");

            while (isRunning)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                Task.Run(() => HandleClientAsync(client));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting server: {ex.Message}");
            // Handle the error gracefully, possibly log the error
        }
        finally
        {
            Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            GameSession session = new GameSession(client, filesDirectory);
            lock (sessionLock)
            {
                gameSessions.Add(session);
            }
            await session.HandleInitialCommunicationAsync();
            // await session.StartGameAsync(); // Handle game communication as needed
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error handling client: {ex.Message}");
            // Handle the error gracefully, possibly log the error
        }
        finally
        {
            lock (sessionLock)
            {
                gameSessions.RemoveAll(s => s.Client == client);
            }
            client.Close();
            Console.WriteLine("Client disconnected.");
        }
    }

    public void Stop()
    {
        try
        {
            isRunning = false;
            tcpListener.Stop();
            Console.WriteLine("Server stopped.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error stopping server: {ex.Message}");
            // Handle the error gracefully, possibly log the error
        }
    }
}

class GameSession
{
    private TcpClient client;
    private NetworkStream stream;
    private readonly string filesDirectory;
    private string currentWordList;
    private object gameLock = new object();

    private int correctGuesses;
    private int totalWords;
    private string[] words;

    public TcpClient Client => client;

    public GameSession(TcpClient client, string filesDirectory)
    {
        this.client = client;
        this.filesDirectory = filesDirectory;
        stream = client.GetStream();
    }

    public async Task HandleInitialCommunicationAsync()
    {
        try
        {

            byte[] initialBuffer = new byte[1024];
            int initialBytesRead = await stream.ReadAsync(initialBuffer, 0, initialBuffer.Length);
            string initialMessage = Encoding.ASCII.GetString(initialBuffer, 0, initialBytesRead).Trim();

            // Split the received message to extract username and time limit
            string[] userData = initialMessage.Split(':');
            if (userData.Length >= 2)
            {
                string username = userData[0].Trim();
                string timeLimitStr = userData[1].Trim();

                // Parse the time limit (assuming it's in seconds)
                if (int.TryParse(timeLimitStr, out int timeLimit))
                {
                    // Process the received user details as needed
                    // For example, store the username and time limit as instance variables

                    Console.WriteLine($"Received Username: {username}");
                    Console.WriteLine($"Received Time Limit: {timeLimit}");

                    // Now start the game
                    await StartGameAsync();
                }
                else
                {
                    // Handle invalid time limit
                    await SendToClientAsync("Invalid time limit format. Please reconnect.");
                    // Close the connection or take appropriate action
                    return;
                }
            }
            else
            {
                // Handle invalid message format
                await SendToClientAsync("Invalid message format. Please reconnect.");
                // Close the connection or take appropriate action
                return;
            }
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Error in initial communication: {ex.Message}");
            // Handle the error gracefully, possibly close connections or log the error
        }
    }

    public async Task StartGameAsync()
    {
        try
        {
            // Reset game state for a new game
            correctGuesses = 0;
            totalWords = 0;
            currentWordList = ""; // Reset current word list to empty

            (string puzzle, int wordsToFind, string[] wordsFromFile) = await GetRandomWordListAsync();
            currentWordList = puzzle;
            totalWords = wordsToFind;
            words = wordsFromFile;

            await SendToClientAsync($"WordList:{currentWordList}");
            await SendToClientAsync($"WordCount:{totalWords}");

            bool gameEnded = false; // New flag to check if the game has ended

            while (correctGuesses < totalWords && !gameEnded)
            {
                try
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    string guess = Encoding.ASCII.GetString(buffer, 0, bytesRead).Trim();

                    await ProcessUserGuessAsync(guess);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"Error reading from client: {ex.Message}");
                    // Handle the error gracefully, possibly close connections or log the error
                }

                // Update gameEnded based on conditions for game end
                gameEnded = (correctGuesses == totalWords); // Modify or add conditions as needed
            }


            await SendToClientAsync("Game Over. \n");
            await SendToClientAsync($"All:{correctGuesses}");

            byte[] responseBuffer = new byte[1024];
            int responseBytesRead = await stream.ReadAsync(responseBuffer, 0, responseBuffer.Length);
            string userResponse = Encoding.ASCII.GetString(responseBuffer, 0, responseBytesRead).Trim();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in game session: {ex.Message}");
        }
    }


    private async Task ProcessUserGuessAsync(string guess)
    {
        if (guess.ToUpper() == "PLAYAGAIN")
        {
            await SendToClientAsync("Starting a new game!");
            correctGuesses = 0; // Reset the game variables or any necessary state
            totalWords = 0;
            // Add any other logic needed to reset the game state
            await StartGameAsync(); // Start a new game
        }
        else if (Array.IndexOf(words, guess) != -1)
        {
            correctGuesses++;
            await SendToClientAsync($"Correct! {correctGuesses}/{totalWords} words found.");
        }
        else
        {
            await SendToClientAsync("Incorrect. Try again.");
        }
    }
    private async Task<(string puzzle, int wordsToFind, string[])> GetRandomWordListAsync()
    {
        try
        {
            string[] files = Directory.GetFiles(filesDirectory, "*.txt");

            if (files.Length == 0)
            {
                throw new InvalidOperationException("No word files found in the specified directory.");
            }

            string randomFilePath = files[new Random().Next(files.Length)];

            string[] lines = await Task.Run(() => File.ReadAllLines(randomFilePath));

            if (lines.Length >= 3)
            {
                string puzzle = lines[0]; // Get the 80-character string to guess
                if (int.TryParse(lines[1], out int wordsToFind))
                {
                    // Get the words starting from the third line
                    string[] words = lines.Skip(2).ToArray();
                    return (puzzle, wordsToFind, words);
                }
            }

            throw new FormatException("Invalid file format. Unable to extract game data.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading word list: {ex.Message}");
            throw;
        }
    }



    private async Task SendToClientAsync(string message)
    {
        try
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error sending to client: {ex.Message}");
            // Handle the error gracefully, possibly close connections or log the error
        }
    }

    private static async Task<string> ReadAllTextAsync(string filePath)
    {
        using (StreamReader reader = new StreamReader(filePath))
        {
            return await reader.ReadToEndAsync();
        }
    }
}

class Program
{
    static void Main(string[] args)
    {
        // Instantiate your server and start it here
        Server server = new Server(); // Example IP address and port
        server.Start();

        // Optionally, keep the console window open
        Console.WriteLine("Press any key to exit...");
        Console.ReadKey();
    }
}
