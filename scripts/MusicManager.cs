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
	private bool _forceLobbyMusic = false;

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
		if (sceneName != _lastSceneName)
		{
			_lastSceneName = sceneName;
			_forceLobbyMusic = false; // Reset on scene change
			GD.Print($"[MusicManager] Scene changed to: {sceneName}");
		}

		// GameWorld is the in-game scene — play last 3 minutes of game track
		// UNLESS we are forcing lobby music (e.g. game over)
		if (sceneName == "GameWorld" && !_forceLobbyMusic)
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

		if (trackName == "game" && _gameTrack != null)
		{
			// Play the last 3 minutes (180s) of the game track
			double trackLength = _gameTrack.GetLength();
			float seekPos = (float)Mathf.Max(0, trackLength - 180.0);
			_player.Play(seekPos);
			GD.Print($"[MusicManager] Now playing game track from {seekPos:F1}s / {trackLength:F1}s (last 3 min)");
		}
		else
		{
			_player.Play();
			GD.Print($"[MusicManager] Now playing: {trackName}");
		}
	}

	/// <summary>
	/// Stops all music. Called when a winner is announced.
	/// </summary>
	public void StopMusic()
	{
		GD.Print("[MusicManager] Music stopped (game over)");
		_player.Stop();
		_currentTrack = "";
	}

	/// <summary>
	/// Forces the lobby music to play even if in GameWorld.
	/// Used for the winning screen.
	/// </summary>
	public void ForceLobbyMusic()
	{
		GD.Print("[MusicManager] Forced lobby music (game over)");
		_forceLobbyMusic = true;
		PlayTrack("lobby");
	}
}
