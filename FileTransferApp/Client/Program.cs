using System.Net.Sockets;

namespace ClientApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var filePath = "example.txt"; // Replace with your file
            var fileName = Path.GetFileName(filePath);
            var serverIp = "127.0.0.1";
            var serverPort = 5000;

            using var client = new TcpClient();
            await client.ConnectAsync(serverIp, serverPort);
            using var networkStream = client.GetStream();

            var nameBuffer = new byte[256];
            Array.Copy(System.Text.Encoding.UTF8.GetBytes(fileName), nameBuffer, fileName.Length);
            await networkStream.WriteAsync(nameBuffer);

            using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(networkStream);

            Console.WriteLine("File sent.");
        }
    }
}
