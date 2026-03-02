using Godot;

/// <summary>
/// Autoload singleton that manages background music across scene transitions.
/// Plays lobby music on title/info/lobby screens and switches to in-game music during gameplay.
/// Persists across scene changes since it is registered as an autoload in project.godot.
/// </summary>
public partial class MusicManager : Node
{
	private AudioStreamPlayer _player;
	private AudioStream _lobbyTrack;
	private AudioStream _gameTrack;
	private string _currentTrack = "";
	private string _lastSceneName = "";

	public override void _Ready()
	{
		// Load both tracks
		_lobbyTrack = GD.Load<AudioStream>("res://assets/sounds/Moonlit Pixel Shores (0.67x).mp3");
		_gameTrack = GD.Load<AudioStream>("res://assets/sounds/Stage Four_ Silent Schemes.mp3");

		if (_lobbyTrack == null) GD.PrintErr("[MusicManager] Failed to load lobby track!");
		if (_gameTrack == null) GD.PrintErr("[MusicManager] Failed to load game track!");

		// Create the audio player
		_player = new AudioStreamPlayer();
		AddChild(_player);
		_player.Bus = "Master";
		_player.Finished += OnFinished;

		// Start lobby music immediately (title screen is first scene)
		PlayTrack("lobby");
	}

	public override void _Process(double delta)
	{
		// Check if the current scene has changed
		var currentScene = GetTree().CurrentScene;
		if (currentScene == null) return;

		string sceneName = currentScene.Name;
		if (sceneName == _lastSceneName) return;

		_lastSceneName = sceneName;
		GD.Print($"[MusicManager] Scene changed to: {sceneName}");

		// GameWorld is the in-game scene — switch to game music
		if (sceneName == "GameWorld")
		{
			PlayTrack("game");
		}
		else
		{
			PlayTrack("lobby");
		}
	}

	private void OnFinished()
	{
		// Loop the current track
		_player.Play();
	}

	private void PlayTrack(string trackName)
	{
		if (_currentTrack == trackName) return;
		_currentTrack = trackName;

		AudioStream track = trackName == "game" ? _gameTrack : _lobbyTrack;
		if (track == null)
		{
			GD.PrintErr($"[MusicManager] Track '{trackName}' is null — check file paths!");
			return;
		}

		_player.Stream = track;
		_player.Play();
		GD.Print($"[MusicManager] Now playing: {trackName}");
	}
}
