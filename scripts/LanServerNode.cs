using Godot;
using System;
using System.IO;
using FatalAttraction.Networking;

public partial class LanServerNode : Node
{
    private LanServer _server;
    private Thread _serverThread;

    public void StartServer(int port)
    {
        string configPath = ProjectSettings.GlobalizePath("res://data/game_configuration.json");
        // In export builds, data might be elsewhere, but for now assuming it's loose or extracting.
        // If packed, we might need to handle it differently, but for LAN prototype:
        if (!File.Exists(configPath))
        {
             // Fallback for editor relative path
             // configPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "data", "game_configuration.json");
        }

        GD.Print($"Starting server on port {port} with config: {configPath}");
        _server = new LanServer(configPath, port);
        _server.Start();
    }

    public void StopServer()
    {
        if (_server != null)
        {
            _server.Stop();
            _server = null;
        }
    }

    public override void _ExitTree()
    {
        StopServer();
    }
}
