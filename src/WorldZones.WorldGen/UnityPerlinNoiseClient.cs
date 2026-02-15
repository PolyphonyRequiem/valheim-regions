using System;
using System.Net.Sockets;
using System.Text;

namespace WorldZones.WorldGen
{
    /// <summary>
    /// Client that connects to Unity PerlinNoiseServer to get exact Mathf.PerlinNoise values.
    /// Requires PerlinNoiseServer to be running in Unity.
    /// </summary>
    public static class UnityPerlinNoiseClient
    {
        private const string HOST = "127.0.0.1";
        private const int PORT = 27015;
        private static TcpClient client;
        private static NetworkStream stream;
        
        /// <summary>
        /// Connects to the Unity Perlin server.
        /// Must be called before GetNoise().
        /// </summary>
        public static bool Connect()
        {
            try
            {
                client = new TcpClient();
                client.Connect(HOST, PORT);
                stream = client.GetStream();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UnityPerlinClient] Failed to connect: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Disconnects from the Unity Perlin server.
        /// </summary>
        public static void Disconnect()
        {
            stream?.Close();
            client?.Close();
            stream = null;
            client = null;
        }
        
        /// <summary>
        /// Gets Perlin noise value from Unity server.
        /// </summary>
        public static float GetNoise(float x, float y)
        {
            if (client == null || !client.Connected)
            {
                throw new InvalidOperationException("Not connected to Unity Perlin server. Call Connect() first.");
            }
            
            try
            {
                // Send request
                string request = $"{x:G17},{y:G17}\n";
                byte[] requestBytes = Encoding.UTF8.GetBytes(request);
                stream.Write(requestBytes, 0, requestBytes.Length);
                
                // Read response
                byte[] buffer = new byte[256];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                
                if (response == "ERROR")
                {
                    throw new Exception("Unity server returned error");
                }
                
                return float.Parse(response);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to get Perlin noise: {ex.Message}", ex);
            }
        }
    }
}
