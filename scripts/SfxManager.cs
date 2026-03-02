using Godot;

/// <summary>
/// Autoload singleton that manages sound effect playback.
/// Preloads all SFX and exposes simple Play methods.
/// </summary>
public partial class SfxManager : Node
{
	private AudioStreamPlayer _punchPlayer;
	private AudioStreamPlayer _chatBeginPlayer;
	private AudioStreamPlayer _chatSuccessPlayer;
	private AudioStreamPlayer _chatFailurePlayer;

	public override void _Ready()
	{
		_punchPlayer = CreatePlayer("res://assets/sounds/soraatwod-punch-416719.mp3");
		_chatBeginPlayer = CreatePlayer("res://assets/sounds/universfield-new-notification-026-380249.mp3");
		_chatSuccessPlayer = CreatePlayer("res://assets/sounds/freesound_crunchpixstudio-purchase-success-384963.mp3");
		_chatFailurePlayer = CreatePlayer("res://assets/sounds/freesound_community-failure-drum-sound-effect-2-7184.mp3");
	}

	private AudioStreamPlayer CreatePlayer(string path)
	{
		var stream = GD.Load<AudioStream>(path);
		if (stream == null)
		{
			GD.PrintErr($"[SfxManager] Failed to load: {path}");
		}

		var player = new AudioStreamPlayer();
		player.Stream = stream;
		player.Bus = "Master";
		AddChild(player);
		return player;
	}

	public void PlayPunch() => _punchPlayer?.Play();
	public void PlayChatBegin() => _chatBeginPlayer?.Play();
	public void PlayChatSuccess() => _chatSuccessPlayer?.Play();
	public void PlayChatFailure() => _chatFailurePlayer?.Play();
}
