using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Client
{
    public partial class GameWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private string receivedPuzzle;
        private int receivedWordsToFind;
        private int remainingTime;
        private DispatcherTimer gameTimer;
        private bool gameActive = true;
        private bool gameEnded = false;
        private int initialTimerValue;


        private const string WordsListPrefix = "WordsList:";
        private const string CorrectPrefix = "Correct!";
        private const string IncorrectPrefix = "Incorrect.";
        private const string TimeRemainingPrefix = "Time Remaining:";
        private const string GameOverPrefix = "Game Over";

        public GameWindow(TcpClient client, NetworkStream stream, string puzzle, int wordsToFind, int userTimerInput)
        {
            InitializeComponent();
            this.client = client;
            this.stream = stream;

            receivedPuzzle = puzzle;
            receivedWordsToFind = wordsToFind;

            txtPuzzle.Text = puzzle;
            txtWordsToFind.Text = $"Words to Find: {receivedWordsToFind}"; // Display word count in the UI

            initialTimerValue = userTimerInput;
            remainingTime = userTimerInput;
            UpdateRemainingTime();

            Task.Run(() => ReceiveGameInfo());

            // Initialize and start the timer
            gameTimer = new DispatcherTimer();
            gameTimer.Interval = TimeSpan.FromSeconds(1);
            gameTimer.Tick += GameTimer_Tick;
            gameTimer.Start();
        }

        private async Task ReceiveGameInfo()
        {
            try
            {
                while (gameActive)
                {
                    byte[] buffer = new byte[1024];
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);

                    ProcessServerMessage(message);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error receiving game information: {ex.Message}", "Error");
            }
        }

        private void ProcessServerMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (message.StartsWith(WordsListPrefix))
                {
                    ProcessWordsListMessage(message);
                }
                else if (message.StartsWith(CorrectPrefix))
                {
                    ProcessCorrectGuessMessage(message);
                }
                else if (message.StartsWith(IncorrectPrefix))
                {
                    ProcessIncorrectGuessMessage(message);
                }
                else if (message.StartsWith(TimeRemainingPrefix))
                {
                    ProcessTimeRemainingMessage(message);
                }
                else if (message.StartsWith(GameOverPrefix))
                {
                    ProcessGameOverMessage(message);
                }
                else if (message.StartsWith("All:")) // Handle the 'All' message here
                {
                    ProcessAllCorrectGuessesMessage(message);
                }
                else if (message == "Starting a new game!")
                {
                    ResetGame();
                }
            });
        }

        private void ProcessAllCorrectGuessesMessage(string message)
        {
            // Extract the number of correct guesses from the message
            string[] parts = message.Split(':');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int totalCorrectGuesses))
            {
                // Do something with the total correct guesses received from the server
                receivedWordsToFind -= totalCorrectGuesses; // Decrement the remaining words to find
                gameEnded = receivedWordsToFind == 0; // Set gameEnded if all words are found
            }
        }


        private void ProcessWordsListMessage(string message)
        {
            string wordList = message.Substring(WordsListPrefix.Length);
            txtPuzzle.Text = wordList;

            // Enable the game window and its components
        }

        private void ProcessCorrectGuessMessage(string message)
        {
            receivedWordsToFind--;
            txtResult.Text = message;
        }

        private void ProcessIncorrectGuessMessage(string message)
        {
            txtResult.Text = message;
        }

        private void ProcessTimeRemainingMessage(string message)
        {
            UpdateRemainingTime();
        }

        private void ProcessGameOverMessage(string message)
        {
            gameActive = false;
            txtAgain.Text = message;
            txtPlayAgain.IsEnabled = true;

            MessageBoxResult result = MessageBox.Show("Do you want to play again?", "Game Over", MessageBoxButton.YesNo);
            if (result == MessageBoxResult.Yes)
            {
                // Reset the timer
                remainingTime = initialTimerValue;
                UpdateRemainingTime();

                // Restart the timer
                gameTimer.Start();

                // Send a message to the server indicating the user wants to play again
                SendPlayAgainMessage();
            }
        }

        private async void SubmitGuess_Click(object sender, RoutedEventArgs e)
        {
            await Dispatcher.Invoke(async () =>
            {
                if (!gameActive)
                {
                    MessageBox.Show("The game is over!", "Game Over");
                    return;
                }

                string guess = txtGuess.Text.Trim();
                if (string.IsNullOrEmpty(guess))
                {
                    MessageBox.Show("Please enter a word!", "Error");
                    return;
                }

                await SendToServerAsync(guess);
                txtGuess.Text = "";
            });
        }


        private void GameTimer_Tick(object sender, EventArgs e)
        {
            remainingTime--;
            UpdateRemainingTime();

            if (remainingTime <= 0 || receivedWordsToFind == 0)
            {
                gameTimer.Stop();
                gameEnded = true; // Set the flag indicating game end
                MessageBox.Show("Time's up! Game over.", "Game Over");
                // Add any other necessary game-over logic here
            }
        }


        private async Task SendToServerAsync(string message)
        {
            try
            {
                byte[] data = Encoding.ASCII.GetBytes(message);
                await stream.WriteAsync(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending message: {ex.Message}", "Error");
            }
        }

        private void UpdateRemainingTime()
        {
            txtRemainingTime.Text = $"Time Remaining: {remainingTime} seconds";
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                this.Close();
            });
        }

        private async void SendPlayAgainMessage()
        {
            try
            {
                // Send a message to the server indicating the user wants to play again
                await SendToServerAsync("PlayAgain");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error sending play again message: {ex.Message}", "Error");
            }
        }

        private void ResetGame()
        {
            // Reset game-specific variables, UI elements, and timer
            remainingTime = initialTimerValue;
            receivedWordsToFind = 0; // Reset any other game-related variables
            gameEnded = false;
            txtPuzzle.Text = "";
            txtResult.Text = "";
            txtAgain.Text = "";
            txtPlayAgain.IsEnabled = false;
            UpdateRemainingTime(); // Reset the timer display
            gameTimer.Start(); // Restart the timer if it's stopped
        }


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Dispose of the NetworkStream and TcpClient
            stream?.Dispose();
            client?.Dispose();
        }
    }
}
