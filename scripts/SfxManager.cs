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
	private AudioStreamPlayer _countdownPlayer;
	private AudioStream _countdownStream;
	private AudioStreamPlayer _massRevPlayer;
	private Tween _massRevTween;
	private const float MASS_REV_NORMAL_DB = 0.0f;

	// Offset in seconds to the start of the third countdown in the track
	// Track is ~47s with 3 countdowns; third starts at roughly 33.5s
	private const float THIRD_COUNTDOWN_OFFSET = 33.5f;

	public override void _Ready()
	{
		_punchPlayer = CreatePlayer("res://assets/sounds/soraatwod-punch-416719.mp3");
		_chatBeginPlayer = CreatePlayer("res://assets/sounds/universfield-new-notification-026-380249.mp3");
		_chatSuccessPlayer = CreatePlayer("res://assets/sounds/freesound_crunchpixstudio-purchase-success-384963.mp3");
		_chatFailurePlayer = CreatePlayer("res://assets/sounds/freesound_community-failure-drum-sound-effect-2-7184.mp3");
		_massRevPlayer = CreatePlayer("res://assets/mass_revelation_sound.mp3");

		_countdownStream = GD.Load<AudioStream>("res://assets/sounds/voicebosch-countdown-from-10-190389.mp3");
		_countdownPlayer = new AudioStreamPlayer();
		_countdownPlayer.Stream = _countdownStream;
		_countdownPlayer.Bus = "Master";
		_countdownPlayer.VolumeDb = 10.0f;
		AddChild(_countdownPlayer);
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
	/// <summary>
	/// Starts the Mass Revelation sound immediately at full volume.
	/// (Music is still audible and fades out separately — no silent gap.)
	/// </summary>
	public void FadeMassRevIn()
	{
		if (_massRevPlayer == null) return;
		_massRevTween?.Kill();
		_massRevPlayer.VolumeDb = MASS_REV_NORMAL_DB;
		_massRevPlayer.Play();
		GD.Print("[SfxManager] Mass Rev started at full volume");
	}

	/// <summary>
	/// Fades the Mass Revelation sound out to silence over <paramref name="durationSec"/> seconds,
	/// then stops the player.
	/// </summary>
	public void FadeMassRevOut(float durationSec)
	{
		if (_massRevPlayer == null) return;
		_massRevTween?.Kill();
		_massRevTween = CreateTween();
		_massRevTween.TweenProperty(_massRevPlayer, "volume_db", -80f, durationSec)
			.SetTrans(Tween.TransitionType.Sine)
			.SetEase(Tween.EaseType.InOut);
		_massRevTween.Finished += () => _massRevPlayer?.Stop();
		GD.Print($"[SfxManager] Mass Rev fading out over {durationSec}s");
	}

	/// <summary>
	/// Plays the third countdown (10 to 1) from the countdown voice track.
	/// </summary>
	public void PlayCountdown()
	{
		if (_countdownPlayer == null || _countdownStream == null) return;
		_countdownPlayer.Play(THIRD_COUNTDOWN_OFFSET);
		GD.Print($"[SfxManager] Playing countdown from offset {THIRD_COUNTDOWN_OFFSET}s");
	}

	/// <summary>
	/// Stops the countdown if it's currently playing.
	/// </summary>
	public void StopCountdown()
	{
		_countdownPlayer?.Stop();
	}
}
