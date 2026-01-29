using Godot;
using System;

/// <summary>
/// Goals menu that displays player objectives overlaid on phone image.
/// Can be shown at game start and reopened via button.
/// </summary>
public partial class GoalsMenu : Control
{
	[Signal]
	public delegate void MenuClosedEventHandler();

	private TextureRect _phoneImage;
	private RichTextLabel _goalsText;
	private Button _closeButton;
	private Font _customFont;
	private string _currentRole = "";
	private string _loveInterest = "";
	private string _targets = "";

	public override void _Ready()
	{
		SetupUI();
		Visible = false;
	}

	private void SetupUI()
	{
		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		// Load phone texture
		var phoneTexture = ResourceLoader.Load<Texture2D>("res://assets/phone-menu.png");
		
		// Make this control full screen to act as overlay
		SetAnchorsPreset(LayoutPreset.FullRect);
		AnchorLeft = 0;
		AnchorTop = 0;
		AnchorRight = 1;
		AnchorBottom = 1;
		OffsetLeft = 0;
		OffsetTop = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		
		// Semi-transparent dark background overlay
		var darkOverlay = new ColorRect();
		darkOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		darkOverlay.Color = new Color(0, 0, 0, 0.7f);
		AddChild(darkOverlay);
		
		// Calculate phone dimensions (scale up the small sprite)
		float phoneScale = 10.0f;
		float phoneWidth = phoneTexture != null ? phoneTexture.GetWidth() * phoneScale : 250;
		float phoneHeight = phoneTexture != null ? phoneTexture.GetHeight() * phoneScale : 500;
		
		// Phone container - manually centered
		var phoneContainer = new Control();
		phoneContainer.CustomMinimumSize = new Vector2(phoneWidth, phoneHeight);
		phoneContainer.Size = new Vector2(phoneWidth, phoneHeight);
		// Center it: assuming 1280x720 viewport, center = (640 - width/2, 360 - height/2)
		// But use anchors for dynamic sizing
		phoneContainer.SetAnchorsPreset(LayoutPreset.Center);
		phoneContainer.OffsetLeft = -phoneWidth / 2;
		phoneContainer.OffsetTop = -phoneHeight / 2;
		phoneContainer.OffsetRight = phoneWidth / 2;
		phoneContainer.OffsetBottom = phoneHeight / 2;
		AddChild(phoneContainer);
		
		// Phone image background
		_phoneImage = new TextureRect();
		_phoneImage.Texture = phoneTexture;
		_phoneImage.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_phoneImage.StretchMode = TextureRect.StretchModeEnum.Scale;
		_phoneImage.Size = new Vector2(phoneWidth, phoneHeight);
		_phoneImage.Position = Vector2.Zero;
		phoneContainer.AddChild(_phoneImage);
		
		// Content panel sized to the actual phone screen pixels (derived from the 64x64 sprite)
		float screenMarginLeft;
		float screenMarginRight;
		float screenMarginTop;
		float screenMarginBottom;
		if (phoneTexture != null && phoneTexture.GetWidth() > 0 && phoneTexture.GetHeight() > 0)
		{
			Vector2 texSize = phoneTexture.GetSize();
			// Screen area in source sprite is x:19-44, y:3-53 (see assets/phone-menu.png)
			screenMarginLeft = phoneWidth * (19.0f / texSize.X);
			screenMarginRight = phoneWidth * ((texSize.X - 1.0f - 44.0f) / texSize.X);
			screenMarginTop = phoneHeight * (3.0f / texSize.Y);
			screenMarginBottom = phoneHeight * ((texSize.Y - 1.0f - 53.0f) / texSize.Y);
		}
		else
		{
			// Fallback margins
			screenMarginLeft = phoneWidth * 0.12f;
			screenMarginRight = phoneWidth * 0.12f;
			screenMarginTop = phoneHeight * 0.06f;
			screenMarginBottom = phoneHeight * 0.12f;
		}
		float screenWidth = phoneWidth - (screenMarginLeft + screenMarginRight);
		float screenHeight = phoneHeight - screenMarginTop - screenMarginBottom;
		
		var contentPanel = new Panel();
		contentPanel.Position = new Vector2(screenMarginLeft, screenMarginTop);
		contentPanel.Size = new Vector2(screenWidth, screenHeight);
		contentPanel.ClipContents = true;
		// Make panel transparent
		var panelStyle = new StyleBoxEmpty();
		contentPanel.AddThemeStyleboxOverride("panel", panelStyle);
		phoneContainer.AddChild(contentPanel);
		
		var contentVBox = new VBoxContainer();
		// Nudge content a bit right to visually center inside the phone screen
		float contentInsetX = phoneWidth * 0.02f;
		contentVBox.Position = new Vector2(contentInsetX, 0);
		contentVBox.Size = new Vector2(screenWidth - (contentInsetX * 2), screenHeight);
		contentVBox.ClipContents = true;
		contentVBox.AddThemeConstantOverride("separation", 8);
		contentPanel.AddChild(contentVBox);

		// Title
		var titleLabel = new Label();
		titleLabel.Text = "YOUR GOALS";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeFontOverride("font", _customFont);
		titleLabel.AddThemeFontSizeOverride("font_size", 32);
		titleLabel.AddThemeColorOverride("font_color", Colors.White);
		contentVBox.AddChild(titleLabel);

		// Goals text area
		_goalsText = new RichTextLabel();
		_goalsText.BbcodeEnabled = true;
		_goalsText.FitContent = false;
		_goalsText.ScrollActive = true;
		_goalsText.ClipContents = true;
		_goalsText.AutowrapMode = TextServer.AutowrapMode.Word;
		_goalsText.CustomMinimumSize = new Vector2(screenWidth, 100);
		_goalsText.SizeFlagsVertical = SizeFlags.ExpandFill;
		_goalsText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_goalsText.AddThemeFontOverride("normal_font", _customFont);
		_goalsText.AddThemeFontOverride("bold_font", _customFont);
		_goalsText.AddThemeColorOverride("default_color", Colors.White);
		_goalsText.AddThemeFontSizeOverride("normal_font_size", 24);
		_goalsText.AddThemeFontSizeOverride("bold_font_size", 26);
		contentVBox.AddChild(_goalsText);

		// Close button (X in phone's middle button at bottom center)
		_closeButton = new Button();
		_closeButton.Text = "";
		_closeButton.AddThemeFontOverride("font", _customFont);
		_closeButton.AddThemeFontSizeOverride("font_size", 30);
		_closeButton.CustomMinimumSize = new Vector2(60, 60);
		_closeButton.Pressed += OnClosePressed;
		// Make button transparent to blend with phone's middle button
		var transparentStyle = new StyleBoxEmpty();
		_closeButton.AddThemeStyleboxOverride("normal", transparentStyle);
		_closeButton.AddThemeStyleboxOverride("hover", transparentStyle);
		_closeButton.AddThemeStyleboxOverride("pressed", transparentStyle);
		_closeButton.AddThemeStyleboxOverride("focus", transparentStyle);
		_closeButton.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.3f, 1.0f));
		// Position close button higher, above the circle button
		_closeButton.Position = new Vector2((phoneWidth - 60) / 2, phoneHeight - 80);
		phoneContainer.AddChild(_closeButton);
	}

	private void OnClosePressed()
	{
		Hide();
		EmitSignal(SignalName.MenuClosed);
	}

	public override void _Input(InputEvent @event)
	{
		if (!Visible)
			return;

		// Close on Escape key
		if (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
		{
			GetViewport().SetInputAsHandled();
			OnClosePressed();
			return;
		}

		// Close when clicking outside the phone
		if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed && mouseEvent.ButtonIndex == MouseButton.Left)
		{
			// Get the phone image's global rect
			if (_phoneImage != null)
			{
				Rect2 phoneRect = _phoneImage.GetGlobalRect();
				if (!phoneRect.HasPoint(mouseEvent.GlobalPosition))
				{
					GetViewport().SetInputAsHandled();
					OnClosePressed();
				}
			}
		}
	}

	/// <summary>
	/// Set the player's role and display appropriate goals.
	/// </summary>
	public void SetRole(string role, string loveInterest = "", string targets = "")
	{
		_currentRole = role;
		_loveInterest = loveInterest;
		_targets = targets;
		UpdateGoalsDisplay();
	}

	private void UpdateGoalsDisplay()
	{
		string goalsContent = "";

		switch (_currentRole.ToLower())
		{
			case "admirer":
				goalsContent = "[center][b]ADMIRER[/b][/center]\n\n";
				goalsContent += "[b]Goals:[/b]\n";
				goalsContent += "• Marry love interest\n";
				goalsContent += "• Kill three targets\n\n";
				
				goalsContent += "[b]Love Interest:[/b]\n";
				if (!string.IsNullOrEmpty(_loveInterest))
				{
					foreach (var name in _loveInterest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
						goalsContent += $"• {name}\n";
					goalsContent += "\n";
				}
				else
					goalsContent += "• (None assigned)\n\n";
				
				goalsContent += "[b]Targets:[/b]\n";
				if (!string.IsNullOrEmpty(_targets))
				{
					foreach (var name in _targets.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
						goalsContent += $"• {name}\n";
				}
				else
					goalsContent += "• (None assigned)\n";
				break;

			case "prophet":
				goalsContent = "[center][b]PROPHET[/b][/center]\n\n";
				goalsContent += "[b]Goals:[/b]\n";
				goalsContent += "• Convert people successfully\n";
				goalsContent += "• Max chaos meter\n";
				goalsContent += "• Achieve high chaos\n";
				break;

			case "producer":
				goalsContent = "[center][b]PRODUCER[/b][/center]\n\n";
				goalsContent += "[b]Goals:[/b]\n";
				goalsContent += "• Catch the Admirer\n";
				goalsContent += "• Stop the Prophet\n";
				goalsContent += "• Max ratings meter\n";
				break;

			default:
				goalsContent = "[center]Waiting for role...[/center]";
				break;
		}

		_goalsText.Text = goalsContent;
	}

	/// <summary>
	/// Show the goals menu.
	/// </summary>
	public void ShowMenu()
	{
		Show();
	}
}
