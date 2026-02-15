using UnityEngine;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// Runs a simple TCP server in Unity that provides Mathf.PerlinNoise values.
/// Allows external C# code to get exact Unity Perlin values without DLL ECall issues.
/// </summary>
public class PerlinNoiseServer : MonoBehaviour
{
    private TcpListener listener;
    private Thread listenerThread;
    private bool isRunning = false;
    private const int PORT = 27015;
    
    void Start()
    {
        StartServer();
    }
    
    void OnDestroy()
    {
        StopServer();
    }
    
    void StartServer()
    {
        isRunning = true;
        listenerThread = new Thread(ListenForClients);
        listenerThread.IsBackground = true;
        listenerThread.Start();
        Debug.Log($"[PerlinServer] Started on port {PORT}");
    }
    
    void StopServer()
    {
        isRunning = false;
        listener?.Stop();
        listenerThread?.Join(1000);
        Debug.Log("[PerlinServer] Stopped");
    }
    
    void ListenForClients()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, PORT);
            listener.Start();
            
            while (isRunning)
            {
                if (listener.Pending())
                {
                    TcpClient client = listener.AcceptTcpClient();
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[PerlinServer] Error: {ex.Message}");
        }
    }
    
    void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[256];
            
            while (client.Connected && isRunning)
            {
                if (stream.DataAvailable)
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                    
                    // Expected format: "x,y" e.g. "0.5,1.2"
                    string[] parts = request.Split(',');
                    if (parts.Length == 2 && 
                        float.TryParse(parts[0], out float x) && 
                        float.TryParse(parts[1], out float y))
                    {
                        float result = Mathf.PerlinNoise(x, y);
                        string response = result.ToString("G17") + "\n";  // Full precision
                        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                        stream.Write(responseBytes, 0, responseBytes.Length);
                    }
                    else
                    {
                        byte[] error = Encoding.UTF8.GetBytes("ERROR\n");
                        stream.Write(error, 0, error.Length);
                    }
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PerlinServer] Client error: {ex.Message}");
        }
        finally
        {
            client?.Close();
        }
    }
}
