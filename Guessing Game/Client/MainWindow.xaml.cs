using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Net;

namespace Client
{
    public partial class MainWindow : Window
    {
        private TcpClient client;
        private NetworkStream stream;
        private int wordsToFind;
        public MainWindow()
        {
            InitializeComponent();
        }

        private async Task<string> ReceiveFromServerAsync()
        {
            byte[] buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
            string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);

            if (message == "Starting a new game!")
            {
                ResetGame();
            }
            return message;
        }


        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string serverIp = txtIP.Text.Trim();
                int port;

                if (!IPAddress.TryParse(serverIp, out _))
                {
                    MessageBox.Show("Invalid IP Address", "Error");
                    return;
                }

                if (!int.TryParse(txtPort.Text, out port) || port <= 0)
                {
                    MessageBox.Show("Invalid Port Number", "Error");
                    return;
                }

                string username = txtUserName.Text.Trim();
                if (string.IsNullOrEmpty(username))
                {
                    MessageBox.Show("Username cannot be empty", "Error");
                    return;
                }

                int timeLimit;
                if (!int.TryParse(txtTime.Text, out timeLimit) || timeLimit <= 0)
                {
                    MessageBox.Show("Invalid Time Limit", "Error");
                    return;
                }

                client = new TcpClient();
                await client.ConnectAsync(serverIp, port);
                stream = client.GetStream();

                string userData = $"{username}:{timeLimit}";
                await SendToServerAsync(userData);

                // Receive the puzzle and word count information from the server
                string messageFromServer = await ReceiveFromServerAsync();

                if (messageFromServer.StartsWith("WordList:"))
                {
                    string puzzleFromServer = messageFromServer.Substring(9); // Extract the puzzle from the message

                    // Receive the word count information from the server
                    string wordCountMessage = await ReceiveFromServerAsync();
                    if (wordCountMessage.StartsWith("WordCount:"))
                    {
                        int wordsToFindFromServer;
                        if (!int.TryParse(wordCountMessage.Substring(10), out wordsToFindFromServer))
                        {
                            // Handle invalid word count format
                            MessageBox.Show("Invalid word count format from server", "Error");
                            return;
                        }

                        int userTimerInput;
                        if (int.TryParse(txtTime.Text, out userTimerInput))
                        {
                            GameWindow gameWindow = new GameWindow(client, stream, puzzleFromServer, wordsToFindFromServer, userTimerInput);
                            gameWindow.Show();
                            this.Close(); // Close MainWindow or perform other necessary actions
                        }
                        else
                        {
                            MessageBox.Show("Invalid timer input. Please enter a valid number.");
                        }
                    }
                }
                else
                {
                    // Handle unexpected message from the server
                    MessageBox.Show("Unexpected message from server", "Error");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Connection error: {ex.Message}", "Error");
            }
        }

        private void ResetGame()
        {
            // Reset UI elements or game-specific variables
            txtIP.Text = "";
            txtPort.Text = "";
            txtUserName.Text = "";
            txtTime.Text = "";
        }


        private async Task SendToServerAsync(string message)
        {
            byte[] data = Encoding.ASCII.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);
        }
    }
}
