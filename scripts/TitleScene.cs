using Godot;
using System;

public partial class TitleScene : Control
{
	private Label _startLabel;
	private Sprite2D _background;
	private Timer _backgroundTimer;
	private Texture2D _bg1;
	private Texture2D _bg2;
	private bool _showingBg1 = true;
	
	// Hover color for buttons
	private Color _normalColor = new Color(1, 1, 1, 1); // White
	private Color _hoverColor = new Color(1, 0.9f, 0.2f, 1); // Yellowish
	
	public override void _Ready()
	{
		_startLabel = GetNode<Label>("StartLabel");
		_background = GetNode<Sprite2D>("Background");
		_backgroundTimer = GetNode<Timer>("BackgroundTimer");
		
		// Load both background textures
		_bg1 = GD.Load<Texture2D>("res://assets/End-BG1.png");
		_bg2 = GD.Load<Texture2D>("res://assets/End-BG2.png");
		
		// Make the label clickable and set up hover signals
		_startLabel.MouseFilter = MouseFilterEnum.Stop;
		_startLabel.MouseEntered += OnStartLabelMouseEntered;
		_startLabel.MouseExited += OnStartLabelMouseExited;
		
		// Connect timer for background animation
		_backgroundTimer.Timeout += OnBackgroundTimerTimeout;
	}
	
	private void OnStartLabelMouseEntered()
	{
		_startLabel.AddThemeColorOverride("font_color", _hoverColor);
	}
	
	private void OnStartLabelMouseExited()
	{
		_startLabel.AddThemeColorOverride("font_color", _normalColor);
	}
	
	private void OnBackgroundTimerTimeout()
	{
		// Alternate between the two background images
		_showingBg1 = !_showingBg1;
		_background.Texture = _showingBg1 ? _bg1 : _bg2;
	}
	
	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
		{
			// Check if click is within the Start label bounds
			if (_startLabel != null)
			{
				var labelRect = _startLabel.GetGlobalRect();
				if (labelRect.HasPoint(mouseEvent.Position))
				{
					OnStartClicked();
				}
			}
		}
	}
	
	private void OnStartClicked()
	{
		GD.Print("Start clicked - transitioning to Info scene");
		GetTree().ChangeSceneToFile("res://scenes/InfoScene.tscn");
	}
}
