using Godot;
using System;

public partial class InfoScene : Control
{
	private const int TotalFrames = 191;

	private Label _nextLabel;
	private Label _backLabel;
	private Label _skipLabel;
	private Label _infoTextLabel;

	private Sprite2D _background;
	private Sprite2D _tvLobby;

	private Timer _backgroundTimer;

	private Texture2D[] _bgFrames;
	private int _currentFrame = 0;

	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1);      // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1);  // Yellowish

	// Array of text messages to cycle through
	private string[] _textMessages = new string[]
	{
		"Welcome to Fatal Attraction!",
		"You've been invited to join the hottest new reality dating show, Love in Paradise!",
		"Play as the Admirer, killing your love interest's potential suitors to win their love...",
		"Or play as the Prophet, spreading your message to save the world...",
		"Or play as the Producer, using your charm to win over the audience and catch the Admirer and Prophet in action.",
	};

	private int _currentTextIndex = 0;

	public override void _Ready()
	{
		_nextLabel = GetNode<Label>("NextLabel");
		_backLabel = GetNode<Label>("BackLabel");
		_skipLabel = GetNode<Label>("SkipLabel");
		_infoTextLabel = GetNode<Label>("InfoText");

		_background = GetNode<Sprite2D>("Background");
		_tvLobby = GetNode<Sprite2D>("TvLobby");
		_backgroundTimer = GetNode<Timer>("BackgroundTimer");

		// Load background frames
		_bgFrames = new Texture2D[TotalFrames];

		int loaded = 0;
		for (int i = 0; i < TotalFrames; i++)
		{
			// If your filenames are zero-padded (e.g., bg_frame_001.png), use ToString("000")
			// string path = $"res://assets/Title_BG_Frames/bg_frame_{(i + 1).ToString("000")}.png";
			string path = $"res://assets/Title_BG_Frames/bg_frame_{i + 1}.png";

			var tex = GD.Load<Texture2D>(path);
			if (tex == null)
				GD.PushWarning($"Missing/failed to load frame: {path}");
			else
				loaded++;

			_bgFrames[i] = tex;
		}

		if (loaded == 0)
		{
			GD.PushError("No background frames loaded. Check folder path / filenames / import settings.");
			return;
		}

		// Set first non-null frame
		_currentFrame = 0;
		while (_currentFrame < TotalFrames && _bgFrames[_currentFrame] == null)
			_currentFrame++;

		if (_currentFrame >= TotalFrames)
		{
			GD.PushError("All background frames are null. Check imports/paths.");
			return;
		}

		_background.Texture = _bgFrames[_currentFrame];

		// Connect timer ONCE, then start
		_backgroundTimer.Timeout += OnBackgroundTimerTimeout;
		_backgroundTimer.WaitTime = 1.0f / 24.0f; // ~24 FPS
		_backgroundTimer.Start();

		// Make the labels clickable and set up hover signals
		SetupLabelHover(_nextLabel);
		SetupLabelHover(_backLabel);
		SetupLabelHover(_skipLabel);

		// Set initial text
		UpdateInfoText();

		// Start with TV and text invisible for fade-in
		_tvLobby.Modulate = new Color(1, 1, 1, 0);
		_infoTextLabel.Modulate = new Color(1, 1, 1, 0);

		// Fade in TV and text
		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_tvLobby, "modulate:a", 1.0f, 0.8f);
		tween.TweenProperty(_infoTextLabel, "modulate:a", 1.0f, 0.8f);
	}

	private void UpdateInfoText()
	{
		if (_currentTextIndex < _textMessages.Length)
			_infoTextLabel.Text = _textMessages[_currentTextIndex];
	}

	private void OnBackgroundTimerTimeout()
	{
		// Find next non-null frame (prevents black background if a frame failed to load)
		for (int tries = 0; tries < TotalFrames; tries++)
		{
			_currentFrame = (_currentFrame + 1) % TotalFrames;
			var tex = _bgFrames[_currentFrame];
			if (tex != null)
			{
				_background.Texture = tex;
				return;
			}
		}

		GD.PushWarning("Background animation: all frames are null.");
	}

	private void SetupLabelHover(Label label)
	{
		if (label == null) return;

		label.MouseFilter = MouseFilterEnum.Stop;
		label.MouseEntered += () => OnLabelMouseEntered(label);
		label.MouseExited += () => OnLabelMouseExited(label);
	}

	private void OnLabelMouseEntered(Label label)
	{
		label.AddThemeColorOverride("font_color", _hoverColor);
	}

	private void OnLabelMouseExited(Label label)
	{
		label.AddThemeColorOverride("font_color", _normalColor);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			if (_nextLabel != null && _nextLabel.GetGlobalRect().HasPoint(mouseEvent.Position))
				OnNextClicked();

			if (_backLabel != null && _backLabel.GetGlobalRect().HasPoint(mouseEvent.Position))
				OnBackClicked();

			if (_skipLabel != null && _skipLabel.GetGlobalRect().HasPoint(mouseEvent.Position))
				OnSkipClicked();
		}
	}

	private void OnNextClicked()
	{
		_currentTextIndex++;

		// If we've shown all messages, fade out TV, text, and buttons, then proceed to lobby
		if (_currentTextIndex >= _textMessages.Length)
		{
			GD.Print("All messages shown - fading out and transitioning to Lobby scene");

			var tween = CreateTween();
			tween.SetParallel(true);
			tween.TweenProperty(_tvLobby, "modulate:a", 0.0f, 0.5f);
			tween.TweenProperty(_infoTextLabel, "modulate:a", 0.0f, 0.5f);
			tween.TweenProperty(_nextLabel, "modulate:a", 0.0f, 0.5f);
			tween.TweenProperty(_backLabel, "modulate:a", 0.0f, 0.5f);
			tween.TweenProperty(_skipLabel, "modulate:a", 0.0f, 0.5f);
			tween.Chain().TweenCallback(Callable.From(() =>
			{
				GetTree().ChangeSceneToFile("res://scenes/Lobby.tscn");
			}));
		}
		else
		{
			UpdateInfoText();
		}
	}

	private void OnBackClicked()
	{
		_currentTextIndex--;

		// If we're before the first message, go back to title scene
		if (_currentTextIndex < 0)
		{
			GD.Print("Going back to Title scene");
			GetTree().ChangeSceneToFile("res://scenes/TitleScene.tscn");
		}
		else
		{
			UpdateInfoText();
		}
	}

	private void OnSkipClicked()
	{
		GD.Print("Skip clicked - transitioning to Lobby scene");

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(_tvLobby, "modulate:a", 0.0f, 0.5f);
		tween.TweenProperty(_infoTextLabel, "modulate:a", 0.0f, 0.5f);
		tween.TweenProperty(_nextLabel, "modulate:a", 0.0f, 0.5f);
		tween.TweenProperty(_backLabel, "modulate:a", 0.0f, 0.5f);
		tween.TweenProperty(_skipLabel, "modulate:a", 0.0f, 0.5f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			GetTree().ChangeSceneToFile("res://scenes/Lobby.tscn");
		}));
	}
}
