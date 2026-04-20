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
	private AudioStreamPlayer _miraclePlayer;
	private AudioStreamPlayer _dontIgnoreMePlayer;
	private AudioStreamPlayer _showMeTheMoneyPlayer;
	private AudioStreamPlayer _wilhelmScreamPlayer;
	private Tween _massRevTween;
	private const float MASS_REV_NORMAL_DB = -6.0f;
	private bool _miracleRepeatActive = false;

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
		_miraclePlayer = CreatePlayer("res://assets/sounds/miracle.mp3");
		_miraclePlayer.VolumeDb = 23.0f;
		_dontIgnoreMePlayer = CreatePlayerFromBytes("res://assets/sounds/not-gonna-be-ignored.mp3");
		_dontIgnoreMePlayer.VolumeDb = 0.0f;

		_wilhelmScreamPlayer = CreatePlayerFromBytes("res://assets/new-character-assets/wilhelmscream.mp3");
		_wilhelmScreamPlayer.VolumeDb = -15.0f;

		_showMeTheMoneyPlayer = CreatePlayerFromBytes("res://assets/sounds/showmethemoney.mp3");
		_showMeTheMoneyPlayer.VolumeDb = 6.0f; // slightly louder so it cuts through the music

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

	/// <summary>
	/// Loads an MP3 file directly from disk bytes, bypassing Godot's import system.
	/// Use this for audio files that haven't been imported by the editor yet.
	/// </summary>
	private AudioStreamPlayer CreatePlayerFromBytes(string resPath)
	{
		var player = new AudioStreamPlayer();
		player.Bus = "Master";

		var file = FileAccess.Open(resPath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PrintErr($"[SfxManager] FileAccess failed for: {resPath} err={FileAccess.GetOpenError()}");
		}
		else
		{
			var bytes = file.GetBuffer((long)file.GetLength());
			file.Close();
			var stream = new AudioStreamMP3();
			stream.Data = bytes;
			player.Stream = stream;
			GD.Print($"[SfxManager] Loaded {bytes.Length} bytes from {resPath}");
		}

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

		// Play miracle sound 3 times evenly across the 10-second ritual
		// Intervals: t=0s, t=3.33s, t=6.67s
		const float interval = 10.0f / 3.0f; // ~3.33s
		_miracleRepeatActive = true;
		_miraclePlayer?.Play();

		var t1 = GetTree().CreateTimer(interval);
		t1.Timeout += () =>
		{
			if (_miracleRepeatActive) _miraclePlayer?.Play();
		};

		var t2 = GetTree().CreateTimer(interval * 2f);
		t2.Timeout += () =>
		{
			if (_miracleRepeatActive) _miraclePlayer?.Play();
		};

		GD.Print("[SfxManager] Mass Rev started at full volume");
	}

	/// <summary>
	/// Fades the Mass Revelation sound out to silence over <paramref name="durationSec"/> seconds,
	/// then stops the player.
	/// </summary>
	public void FadeMassRevOut(float durationSec)
	{
		if (_massRevPlayer == null) return;
		_miracleRepeatActive = false; // cancel any pending miracle repeats
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

	/// <summary>
	/// Plays the Admirer's "Not gonna be ignored" line and ducks music volume.
	/// </summary>
	public void PlayDontIgnoreMe()
	{
		if (_dontIgnoreMePlayer == null) return;
		
		_dontIgnoreMePlayer.Play();
		
		// Duck music signficantly so the voice line is clearly audible
		var music = GetNodeOrNull<MusicManager>("/root/MusicManager");
		if (music != null)
		{
			music.FadeTo(-35.0f, 0.15f);
			
			// Restore music after delay (approx length of the clip)
			var timer = GetTree().CreateTimer(1.5f);
			timer.Timeout += () => {
				music.FadeTo(0.0f, 0.6f);
			};
		}
	}

	/// <summary>
	/// Plays the Producer's "Show Me The Money" voice line and ducks the music slightly.
	/// </summary>
	public void PlayBehold()
	{
		if (_showMeTheMoneyPlayer == null) return;
		_showMeTheMoneyPlayer.Stop(); // reset if already playing
		_showMeTheMoneyPlayer.Play();

		// Duck music while the line plays
		var music = GetNodeOrNull<MusicManager>("/root/MusicManager");
		if (music != null)
		{
			music.FadeTo(-10.0f, 0.15f);
			// Restore after ~3 s (generous estimate for clip length)
			var timer = GetTree().CreateTimer(3.0f);
			timer.Timeout += () => music.FadeTo(0.0f, 0.6f);
		}
	}

	/// <summary>
	/// Stops the "Show Me The Money" voice line early if needed.
	/// </summary>
	public void StopBehold() => _showMeTheMoneyPlayer?.Stop();

	/// <summary>
	/// Plays the iconic Wilhelm Scream sound effect.
	/// </summary>
	public void PlayWilhelmScream()
	{
		_wilhelmScreamPlayer?.Play();
	}
}
