using Microsoft.Win32;
using System;
using System.Windows;
using Client; // uses FileSender
using System.IO.Compression;
using System.Windows.Controls;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace GUI
{
    public partial class MainWindow : Window
    {
        private List<string> selectedFiles = new List<string>();
        private string selectedFolder = null;
        private System.Threading.CancellationTokenSource? serverCts = null;
        private Task? serverTask = null;
        private string usersFile = "users.txt";
        private string receivedFilesDir;
        private string settingsFile = "settings.txt";

        public MainWindow()
        {
            InitializeComponent();
            // Set the received files directory to Documents/shared
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            receivedFilesDir = Path.Combine(documentsPath, "shared");
            Directory.CreateDirectory(receivedFilesDir);
            // Populate received folder path combo box
            if (ReceivedPathCombo != null)
            {
                ReceivedPathCombo.ItemsSource = new[] { receivedFilesDir };
                ReceivedPathCombo.SelectedIndex = 0;
            }

            // Load last used settings
            LoadSettings();

            // Add window closing event handler
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Stop the server if it's running
            if (serverTask != null && !serverTask.IsCompleted)
            {
                serverCts?.Cancel();
                try
                {
                    serverTask.Wait(1000); // Wait up to 1 second for server to stop
                }
                catch { /* Ignore any errors during shutdown */ }
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(settingsFile))
                {
                    var settings = File.ReadAllLines(settingsFile);
                    if (settings.Length >= 2)
                    {
                        IpBox.Text = settings[0];
                        UsernameBox.Text = settings[1];
                    }
                }
            }
            catch { /* Ignore any errors loading settings */ }
        }

        private void SaveSettings()
        {
            try
            {
                File.WriteAllLines(settingsFile, new[] { IpBox.Text, UsernameBox.Text });
            }
            catch { /* Ignore any errors saving settings */ }
        }

        private void Browse_Click(object? sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Select file(s) or choose a folder"
            };
            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                selectedFiles = new List<string>(dialog.FileNames);
                selectedFolder = null;
                FilePathBox.Text = string.Join(", ", selectedFiles);
            }
            else
            {
                // Use fully qualified name for FolderBrowserDialog
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog();
                var result = folderDialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    selectedFolder = folderDialog.SelectedPath;
                    selectedFiles.Clear();
                    FilePathBox.Text = selectedFolder;
                }
            }
        }

        private async void SendFile_Click(object? sender, RoutedEventArgs e)
        {
            Console.WriteLine("[GUI] SendFile_Click started");
            // Save settings before sending
            SaveSettings();

            var ip = IpBox.Text.Trim();
            var user = UsernameBox.Text.Trim();
            var pass = HashPassword(PasswordBox.Password);

            if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(user))
            {
                StatusText.Text = "Please provide server IP and username.";
                return;
            }

            bool zipRequested = ZipCheckbox.IsChecked == true;

            // Folder send must be zipped
            if (selectedFolder != null && !Directory.Exists(selectedFolder))
            {
                StatusText.Text = "Selected folder does not exist.";
                return;
            }

            if (selectedFolder == null && selectedFiles.Count == 0)
            {
                StatusText.Text = "No file or folder selected.";
                return;
            }

            // Build list of files to send (could be 1 zip or many individual files)
            List<(string path,string display)> filesToSend = new();
            List<string> tempZips = new();

            if (selectedFolder != null)
            {
                // Always zip folder
                string zipName = string.IsNullOrWhiteSpace(ZipNameBox.Text) ? new DirectoryInfo(selectedFolder).Name + ".zip" : ZipNameBox.Text.Trim();
                string tempZip = Path.Combine(Path.GetTempPath(), $"send_{Guid.NewGuid()}_{zipName}");
                Console.WriteLine($"[GUI] Zipping folder: {selectedFolder} -> {tempZip}");
                ZipFile.CreateFromDirectory(selectedFolder, tempZip);
                filesToSend.Add((tempZip, zipName));
                tempZips.Add(tempZip);
            }
            else
            {
                if (zipRequested)
                {
                    string zipName = string.IsNullOrWhiteSpace(ZipNameBox.Text) ? "files.zip" : ZipNameBox.Text.Trim();
                    string tempZip = Path.Combine(Path.GetTempPath(), $"send_{Guid.NewGuid()}_{zipName}");
                    Console.WriteLine($"[GUI] Zipping files: {string.Join(", ", selectedFiles)} -> {tempZip}");
                    using var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create);
                    foreach (var file in selectedFiles)
                        zip.CreateEntryFromFile(file, Path.GetFileName(file));
                    filesToSend.Add((tempZip, zipName));
                    tempZips.Add(tempZip);
                }
                else
                {
                    foreach (var file in selectedFiles)
                        filesToSend.Add((file, Path.GetFileName(file)));
                }
            }

            SendProgressBar.Value = 0;
            SendProgressBar.Visibility = Visibility.Visible;

            var overallSuccess = true;
            foreach (var (path, display) in filesToSend)
            {
                StatusText.Text = $"Sending {display} ...";
                Console.WriteLine($"[GUI] Sending file: {path} as {display} to {ip} as user {user}");
                try
                {
                    var progress = new Progress<double>(p => SendProgressBar.Value = p);
                    var msg = await FileSender.SendFileAsync(path, display, ip, 5000, user, pass, progress);
                    Console.WriteLine($"[GUI] SendFileAsync success: {msg}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GUI] SendFileAsync error: {ex}");
                    StatusText.Text = $"Error sending {display}: {ex.Message}";
                    overallSuccess = false;
                    break;
                }
            }

            SendProgressBar.Visibility = Visibility.Collapsed;
            if (overallSuccess)
                StatusText.Text = "All file(s) sent successfully.";

            // Clean up temp zips
            foreach (var tmp in tempZips)
                if (File.Exists(tmp))
                {
                    Console.WriteLine($"[GUI] Deleting temp zip: {tmp}");
                    File.Delete(tmp);
                }

            Console.WriteLine("[GUI] SendFile_Click finished");
        }

        // Server tab event handlers
        private async void StartServerButton_Click(object? sender, RoutedEventArgs e)
        {
            if (serverTask != null && !serverTask.IsCompleted)
            {
                ServerStatusText.Text = "Server already running.";
                return;
            }
            serverCts = new System.Threading.CancellationTokenSource();
            serverTask = Task.Run(() => RunServer(serverCts.Token));
            ServerStatusText.Text = "Server started.";
            ServerStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
        }

        private void StopServerButton_Click(object? sender, RoutedEventArgs e)
        {
            serverCts?.Cancel();
            ServerStatusText.Text = "Server stopped.";
            ServerStatusIndicator.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
        }

        private void AddUserButton_Click(object? sender, RoutedEventArgs e)
        {
            string username = NewUsernameBox.Text.Trim();
            string password = NewPasswordBox.Password.Trim();

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                System.Windows.MessageBox.Show("Please enter both username and password.");
                return;
            }

            string hashedPassword = HashPassword(password);

            if (File.Exists(usersFile))
            {
                var users = File.ReadAllLines(usersFile).ToList();
                if (users.Any(u => u.Split(':')[0] == username))
                {
                    ServerStatusText.Text = "User already exists.";
                    return;
                }
                users.Add($"{username}:{hashedPassword}");
                File.WriteAllLines(usersFile, users);
            }
            else
            {
                File.AppendAllLines(usersFile, new[] { $"{username}:{hashedPassword}" });
            }
            ServerStatusText.Text = $"User {username} added.";
            RefreshUsersList();
            NewUsernameBox.Clear();
            NewPasswordBox.Clear();
        }

        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password.Trim()));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private void RemoveUserButton_Click(object? sender, RoutedEventArgs e)
        {
            if (UserListBox.SelectedItem is string selected)
            {
                var username = selected.Split(':')[0];
                var users = File.Exists(usersFile) ? File.ReadAllLines(usersFile).ToList() : new List<string>();
                users = users.Where(u => u.Split(':')[0] != username).ToList();
                File.WriteAllLines(usersFile, users);
                ServerStatusText.Text = $"User {username} removed.";
                RefreshUsersList();
            }
        }

        private void RefreshUsersButton_Click(object? sender, RoutedEventArgs e) => RefreshUsersList();

        private void RefreshUsersList()
        {
            if (File.Exists(usersFile))
                UserListBox.ItemsSource = File.ReadAllLines(usersFile);
            else
                UserListBox.ItemsSource = Array.Empty<string>();
        }

        private void RefreshFilesButton_Click(object? sender, RoutedEventArgs e)
        {
            if (Directory.Exists(receivedFilesDir))
            {
                var files = Directory.GetFiles(receivedFilesDir)
                    .Select(f => $"{Path.GetFileName(f)} ({new FileInfo(f).Length} bytes)")
                    .ToList();
                ReceivedFilesListBox.ItemsSource = files;
            }
            else
            {
                ReceivedFilesListBox.ItemsSource = Array.Empty<string>();
            }
        }

        // Drag-and-drop stubs
        private void FilePathBox_Drop(object? sender, System.Windows.DragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                selectedFiles = files.ToList();
                selectedFolder = null;
                FilePathBox.Text = string.Join(", ", selectedFiles);
            }
        }

        private void ReceivedFilesListBox_Drop(object? sender, System.Windows.DragEventArgs e)
        {
            // Optionally implement drag-out to desktop, or drag-in to move files
        }

        // Server logic (simple TCP server with user/pass auth)
        private void RunServer(System.Threading.CancellationToken token)
        {
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, Protocol.Port);
            listener.Start();
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (!listener.Pending())
                    {
                        System.Threading.Thread.Sleep(100);
                        continue;
                    }
                    var client = listener.AcceptTcpClient();
                    Task.Run(() => HandleClient(client));
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        private void HandleClient(System.Net.Sockets.TcpClient client)
        {
            // Load certificate (server.pfx) located next to executable
            X509Certificate2 cert;
            try
            {
                cert = new X509Certificate2("server.pfx", "changeit");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] Failed to load certificate: {ex}");
                client.Close();
                return;
            }

            using var ssl = new SslStream(client.GetStream(), false);
            try
            {
                ssl.AuthenticateAsServer(cert, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Server] TLS handshake failed: {ex}");
                client.Close();
                return;
            }

            // ==== Nonce handshake ====
            // 1. Send nonce to client
            var nonce = RandomNumberGenerator.GetBytes(Protocol.NonceSize);
            ssl.Write(nonce);

            // 2. Read username block + hmac
            var userBuf = new byte[Protocol.UserBlock];
            ReadExact(ssl, userBuf);
            var username = System.Text.Encoding.UTF8.GetString(userBuf).TrimEnd('\0');
            var hmacBuf = new byte[Protocol.HmacBlock];
            ReadExact(ssl, hmacBuf);

            // 3. Validate user
            string storedHash = GetUserHash(username);
            bool ok = false;
            if (storedHash != null)
            {
                try
                {
                    byte[] key = Convert.FromBase64String(storedHash);
                    using var hmac = new HMACSHA256(key);
                    byte[] expected = hmac.ComputeHash(nonce);
                    ok = CryptographicOperations.FixedTimeEquals(expected, hmacBuf);
                }
                catch { ok = false; }
            }

            ssl.Write(System.Text.Encoding.UTF8.GetBytes(ok ? Protocol.AuthOk : Protocol.AuthFail));
            if (!ok)
            {
                client.Close();
                return;
            }

            // ==== Receive filename ====
            var nameBuffer = new byte[Protocol.FileNameBlock];
            ReadExact(ssl, nameBuffer);
            var fileName = System.Text.Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0');

            // ==== Save incoming file ====
            Directory.CreateDirectory(receivedFilesDir);
            var savePath = System.IO.Path.Combine(receivedFilesDir, fileName);
            using var fileStream = File.Create(savePath);
            ssl.CopyTo(fileStream);

            // Send final SUCCESS acknowledgment
            ssl.Write(System.Text.Encoding.UTF8.GetBytes("SUCCESS"));
            Dispatcher.Invoke(RefreshFilesButton_Click, this, new RoutedEventArgs());
        }

        private string GetUserHash(string username)
        {
            if (!File.Exists(usersFile)) return null;
            foreach (var line in File.ReadAllLines(usersFile))
            {
                var parts = line.Split(':');
                if (parts.Length == 2 && parts[0] == username)
                    return parts[1];
            }
            return null;
        }

        private void ReadExact(System.IO.Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = stream.Read(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new IOException("Client disconnected prematurely.");
                offset += read;
            }
        }

        private bool IsUserValid(string username, string password)
        {
            if (!File.Exists(usersFile)) return false;
            return File.ReadAllLines(usersFile).Any(line =>
            {
                var parts = line.Split(':');
                return parts.Length == 2 && parts[0] == username && parts[1] == password;
            });
        }

        private void TabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (e.Source is System.Windows.Controls.TabControl tabControl && tabControl.SelectedIndex == 1) // Server tab
            {
                RefreshUsersList();
                RefreshFilesButton_Click(this, new RoutedEventArgs());
            }
        }

        private void DeleteFileButton_Click(object? sender, RoutedEventArgs e)
        {
            if (ReceivedFilesListBox.SelectedItem is string selectedFile)
            {
                var filePath = System.IO.Path.Combine(receivedFilesDir, selectedFile);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    RefreshFilesButton_Click(this, new RoutedEventArgs());
                }
            }
        }

        private void OpenReceivedFolderButton_Click(object? sender, RoutedEventArgs e)
        {
            try
            {
                if (Directory.Exists(receivedFilesDir))
                {
                    Process.Start("explorer.exe", receivedFilesDir);
                }
                else
                {
                    System.Windows.MessageBox.Show($"Directory '{receivedFilesDir}' does not exist.");
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to open directory: {ex.Message}");
            }
        }

        // Zip checkbox handlers
        private void ZipCheckbox_Checked(object? sender, RoutedEventArgs e)
        {
            if (ZipNameBox != null)
            {
                ZipNameBox.IsEnabled = true;
                if (string.IsNullOrWhiteSpace(ZipNameBox.Text))
                    ZipNameBox.Text = "files.zip";
            }
        }

        private void ZipCheckbox_Unchecked(object? sender, RoutedEventArgs e)
        {
            if (ZipNameBox != null)
            {
                ZipNameBox.IsEnabled = false;
            }
        }
    }
}
