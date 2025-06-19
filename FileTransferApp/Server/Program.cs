using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ServerApp
{
    class Program
    {
        private static readonly string PrivateKey = "mysecretkey"; // TODO: Make configurable

        static async Task Main(string[] args)
        {
            var listener = new TcpListener(IPAddress.Any, 5000);
            listener.Start();
            Console.WriteLine("Server is listening on port 5000...");

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = HandleClientAsync(client); // fire-and-forget per connection
            }
        }

        private static async Task HandleClientAsync(TcpClient client)
        {
            Console.WriteLine("Client connected");

            using var stream = client.GetStream();

            // Step 1: Read 256 bytes of auth
            var authBuffer = new byte[256];
            await ReadExactAsync(stream, authBuffer);
            var auth = Encoding.UTF8.GetString(authBuffer).TrimEnd('\0');
            Console.WriteLine($"Auth received: {auth}");

            // Auth check (expecting username:hashedpassword)
            var parts = auth.Split(':');
            if (parts.Length != 2 || !IsUserValid(parts[0], parts[1]))
            {
                Console.WriteLine("Auth failed. Connection closed.");
                await stream.WriteAsync(Encoding.UTF8.GetBytes("AUTH_FAIL"));
                client.Close();
                return;
            }
            Console.WriteLine($"Auth succeeded for user: {parts[0]}");

            // Step 2: Read 256 bytes of filename
            var nameBuffer = new byte[256];
            await ReadExactAsync(stream, nameBuffer);
            var fileName = Encoding.UTF8.GetString(nameBuffer).TrimEnd('\0');
            Console.WriteLine($"Receiving file: {fileName}");

            // Step 3: Save file (zip or not)
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string receivedFilesDir = Path.Combine(documentsPath, "shared");
            string savePath = Path.Combine(receivedFilesDir, fileName);
            Directory.CreateDirectory(receivedFilesDir);
            Console.WriteLine($"Saving to: {Path.GetFullPath(savePath)}");

            long totalBytes = 0;
            using (var fileStream = File.Create(savePath))
            {
                byte[] buffer = new byte[81920];
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read);
                    totalBytes += read;
                }
            }
            Console.WriteLine($"File received: {fileName}, Size: {totalBytes} bytes");

            // Send success acknowledgment
            await stream.WriteAsync(Encoding.UTF8.GetBytes("SUCCESS"));

            Console.WriteLine($"Saved to {savePath}");
        }

        private static bool IsUserValid(string username, string passwordHash)
        {
            string usersFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "users.txt");
            if (!File.Exists(usersFile)) return false;
            foreach (var line in File.ReadAllLines(usersFile))
            {
                var parts = line.Split(':');
                if (parts.Length == 2 && parts[0] == username && parts[1] == passwordHash)
                    return true;
            }
            return false;
        }

        private static string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new IOException("Client disconnected prematurely.");
                offset += read;
            }
        }
    }
}
