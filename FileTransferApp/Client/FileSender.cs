using System;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Net.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

namespace Client
{
    public class FileSender
    {
        public static async Task<string> SendFileAsync(string filePath, string displayName, string serverIp, int port, string username, string password, IProgress<double> progress = null)
        {
            Console.WriteLine($"[Client] Connecting to {serverIp}:{port}");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File does not exist.", filePath);

            var fileName = displayName ?? Path.GetFileName(filePath);

            using var client = new TcpClient();
            await client.ConnectAsync(serverIp, port);

            // Wrap in TLS
            using var ssl = new SslStream(client.GetStream(), false, (sender, cert, chain, errors) => true);
            await ssl.AuthenticateAsClientAsync(serverIp, null, SslProtocols.Tls12 | SslProtocols.Tls13, false);

            // ==== One-time token auth ====
            // 1. Receive nonce from server
            var nonce = new byte[Protocol.NonceSize];
            await ReadExactAsync(ssl, nonce);

            // 2. Compute HMAC(nonce, key=passwordHashBytes)
            byte[] key = Convert.FromBase64String(password); // password is already SHA-256 hashed & Base64 encoded
            using var hmac = new HMACSHA256(key);
            byte[] hmacBytes = hmac.ComputeHash(nonce); // 32 bytes

            // 3. Send username block + hmac
            byte[] userBytes = Encoding.UTF8.GetBytes(username);
            if (userBytes.Length > Protocol.UserBlock)
                throw new InvalidOperationException("Username too long");
            Array.Resize(ref userBytes, Protocol.UserBlock); // pads with zeros
            await ssl.WriteAsync(userBytes);
            await ssl.WriteAsync(hmacBytes);

            // 4. Wait for auth result
            var authRespBuf = new byte[Protocol.AuthOk.Length];
            await ReadExactAsync(ssl, authRespBuf);
            var authResp = Encoding.UTF8.GetString(authRespBuf).TrimEnd('\0');
            Console.WriteLine($"[Client] Auth response: {authResp}");
            if (authResp != Protocol.AuthOk)
                throw new IOException("Authentication failed. Check your username and password.");

            // Send filename
            byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
            if (nameBytes.Length > Protocol.FileNameBlock)
                throw new InvalidOperationException("Filename too long");
            Array.Resize(ref nameBytes, Protocol.FileNameBlock);
            Console.WriteLine($"[Client] Sending filename: {fileName}");
            await ssl.WriteAsync(nameBytes);

            // Send file content with progress
            using var fileStream = File.OpenRead(filePath);
            var buffer = new byte[81920];
            long total = fileStream.Length;
            long sent = 0;
            int read;
            Console.WriteLine($"[Client] Sending file data: {fileName}, Size: {total} bytes");
            while ((read = await fileStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await ssl.WriteAsync(buffer.AsMemory(0, read));
                sent += read;
                progress?.Report((double)sent * 100 / total);
            }
            // Signal to the server that we have finished sending data so it can finish reading
            client.Client.Shutdown(SocketShutdown.Send);
            Console.WriteLine($"[Client] File data sent: {sent} bytes");

            // Wait for server acknowledgment
            var responseBuffer = new byte[7]; // "SUCCESS" is 7 bytes
            Console.WriteLine("[Client] Waiting for server acknowledgment...");
            await ReadExactAsync(ssl, responseBuffer);
            var response = Encoding.UTF8.GetString(responseBuffer);
            Console.WriteLine($"[Client] Server response: {response}");
            if (response != "SUCCESS")
                throw new IOException("Server did not acknowledge successful file transfer");

            return $"File '{fileName}' sent to {serverIp}:{port}";
        }

        private static async Task ReadExactAsync(Stream stream, byte[] buffer)
        {
            int offset = 0;
            while (offset < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
                if (read == 0) throw new IOException("Server disconnected prematurely");
                offset += read;
            }
        }
    }
}
