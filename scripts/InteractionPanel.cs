using Godot;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Modal panel that displays action buttons when a player interacts with an NPC.
/// Shows role-appropriate actions and handles action submission.
/// </summary>
public partial class InteractionPanel : PanelContainer
{
	[Signal]
	public delegate void ActionSelectedEventHandler(string npcId, string actionId);

	[Signal]
	public delegate void PanelClosedEventHandler();

	// UI Elements
	private Label _npcNameLabel;
	private Label _interactionStatusLabel;
	private TextureRect _portraitSprite;
	private TextureRect _messageBox;
	private Container _actionsContainer;
	private Button _closeButton;
	private ScrollContainer _actionsScrollContainer;

	// Current state
	private string _currentNpcId;
	private Font _customFont;

	public override void _Ready()
	{
		SetupUI();
		Hide(); // Hidden by default
	}

	private void SetupUI()
	{
		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		// Remove panel background
		var styleEmpty = new StyleBoxEmpty();
		AddThemeStyleboxOverride("panel", styleEmpty);

		// Main VBox to position content at bottom
		var mainVBox = new VBoxContainer();
		mainVBox.AddThemeConstantOverride("separation", 0);
		AddChild(mainVBox);

		// Top spacer to push content down
		var topSpacer = new Control();
		topSpacer.SizeFlagsVertical = SizeFlags.ExpandFill;
		mainVBox.AddChild(topSpacer);

		// HBox to hold message box and portrait side by side
		var mainHBox = new HBoxContainer();
		mainHBox.AddThemeConstantOverride("separation", 20);
		mainVBox.AddChild(mainHBox);

		// Left side: Message box with content
		var messageBoxContainer = new Control();
		messageBoxContainer.CustomMinimumSize = new Vector2(974, 468);
		mainHBox.AddChild(messageBoxContainer);

		// Message box background (npc-message-box.png)
		_messageBox = new TextureRect();
		_messageBox.Texture = ResourceLoader.Load<Texture2D>("res://assets/npc-message-box.png");
		_messageBox.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
		_messageBox.ExpandMode = TextureRect.ExpandModeEnum.FitWidth;
		_messageBox.CustomMinimumSize = new Vector2(974, 468);
		messageBoxContainer.AddChild(_messageBox);

		// Content area inside the message box
		var contentMargin = new MarginContainer();
		contentMargin.AddThemeConstantOverride("margin_left", 25);
		contentMargin.AddThemeConstantOverride("margin_top", 20);
		contentMargin.AddThemeConstantOverride("margin_right", 25);
		contentMargin.AddThemeConstantOverride("margin_bottom", 20);
		contentMargin.CustomMinimumSize = new Vector2(974, 468);
		messageBoxContainer.AddChild(contentMargin);

		// Content VBox
		var contentVBox = new VBoxContainer();
		contentVBox.AddThemeConstantOverride("separation", 5);
		contentMargin.AddChild(contentVBox);

		// Status label at top of message box
		var statusMargin = new MarginContainer();
		statusMargin.AddThemeConstantOverride("margin_left", 500);
		contentVBox.AddChild(statusMargin);

		_interactionStatusLabel = new Label();
		_interactionStatusLabel.Text = "NPC awaits your action";
		_interactionStatusLabel.AddThemeFontOverride("font", _customFont);
		_interactionStatusLabel.AddThemeFontSizeOverride("font_size", 24);
		_interactionStatusLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.2f, 0.2f));
		statusMargin.AddChild(_interactionStatusLabel);

		// Separator
		var separator = new HSeparator();
		contentVBox.AddChild(separator);

		// Actions label
		var actionsLabel = new Label();
		actionsLabel.Text = "What will you do?";
		actionsLabel.AddThemeFontOverride("font", _customFont);
		actionsLabel.AddThemeFontSizeOverride("font_size", 24);
		actionsLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.2f, 0.2f));
		
		var actionsMargin = new MarginContainer();
		actionsMargin.AddThemeConstantOverride("margin_left", 110);
		actionsMargin.AddChild(actionsLabel);
		contentVBox.AddChild(actionsMargin);

		// Scroll container for actions
		var scrollMargin = new MarginContainer();
		scrollMargin.AddThemeConstantOverride("margin_left", 110);
		scrollMargin.SizeFlagsVertical = SizeFlags.ExpandFill;
		
		_actionsScrollContainer = new ScrollContainer();
		_actionsScrollContainer.CustomMinimumSize = new Vector2(0, 120);
		_actionsScrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollMargin.AddChild(_actionsScrollContainer);

		_actionsContainer = new HBoxContainer();
		_actionsContainer.AddThemeConstantOverride("separation", 10);
		_actionsScrollContainer.AddChild(_actionsContainer);
		
		// Close button - added to actions container in ShowForNPC
		_closeButton = new Button();
		_closeButton.Text = "Close (ESC)";
		_closeButton.AddThemeFontOverride("font", _customFont);
		_closeButton.AddThemeFontSizeOverride("font_size", 24);
		_closeButton.CustomMinimumSize = new Vector2(0, 40);
		_closeButton.Pressed += OnClosePressed;
		
		contentVBox.AddChild(scrollMargin);

		// Right side: Portrait with NPC name overlay
		var portraitContainer = new Control();
		portraitContainer.CustomMinimumSize = new Vector2(224, 655);
		mainHBox.AddChild(portraitContainer);

		// Portrait sprite
		_portraitSprite = new TextureRect();
		_portraitSprite.Texture = ResourceLoader.Load<Texture2D>("res://assets/sprite-portrait.png");
		_portraitSprite.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
		_portraitSprite.CustomMinimumSize = new Vector2(224, 655);
		portraitContainer.AddChild(_portraitSprite);

		// NPC name on top of portrait
		var nameMargin = new MarginContainer();
		nameMargin.AddThemeConstantOverride("margin_top", -80);
		nameMargin.CustomMinimumSize = new Vector2(160, 50);
		portraitContainer.AddChild(nameMargin);

		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcNameLabel.VerticalAlignment = VerticalAlignment.Top;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 28);
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.White);
		_npcNameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_npcNameLabel.AddThemeConstantOverride("outline_size", 3);
		_npcNameLabel.Visible = false;
		nameMargin.AddChild(_npcNameLabel);
	}

	/// <summary>
	/// Show the interaction panel for a specific NPC with available actions
	/// </summary>
	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions)
	{
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;
		_interactionStatusLabel.Text = $"{npcName} awaits your action.";

		// Portrait sprite is already loaded, could be customized per NPC later
		// For now, it uses the default sprite-portrait.png

		// Clear previous actions
		foreach (Node child in _actionsContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Count visible actions (non-RPS moves)
		int visibleActionCount = 0;
		if (actions != null)
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				if (!actionId.EndsWith("_rock") && !actionId.EndsWith("_paper") && !actionId.EndsWith("_scissors"))
				{
					visibleActionCount++;
				}
			}
		}

		// Switch container type based on button count (more than 2 buttons = vertical)
		Container oldContainer = _actionsContainer;
		if (visibleActionCount > 2)
		{
			_actionsContainer = new VBoxContainer();
			_actionsContainer.AddThemeConstantOverride("separation", 10);
		}
		else
		{
			_actionsContainer = new HBoxContainer();
			_actionsContainer.AddThemeConstantOverride("separation", 10);
		}

		// Replace the old container with the new one
		_actionsScrollContainer.RemoveChild(oldContainer);
		oldContainer.QueueFree();
		_actionsScrollContainer.AddChild(_actionsContainer);

		// Create action buttons
		if (actions == null || actions.Count == 0)
		{
			var noActionsLabel = new Label();
			noActionsLabel.Text = "No actions available for this NPC.";
			noActionsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			noActionsLabel.AddThemeFontOverride("font", _customFont);
			noActionsLabel.AddThemeColorOverride("font_color", new Color(0.2f, 0.2f, 0.2f));
			_actionsContainer.AddChild(noActionsLabel);
		}
		else
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				
				// FILTER: Don't show RPS moves in the text box (handled by bottom bar)
				if (actionId.EndsWith("_rock") || actionId.EndsWith("_paper") || actionId.EndsWith("_scissors"))
					continue;

				string actionText = action["text"]?.Value<string>() ?? "Unknown Action";

				var actionButton = new Button();
				actionButton.Text = actionText;
				actionButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
				actionButton.AddThemeFontOverride("font", _customFont);
				actionButton.AddThemeFontSizeOverride("font_size", 24);
				actionButton.CustomMinimumSize = new Vector2(0, 40);
				
				// Capture the actionId for the lambda
				string capturedActionId = actionId;
				actionButton.Pressed += () => OnActionPressed(capturedActionId);
				
				_actionsContainer.AddChild(actionButton);
			}
		}
		
		// Add close button at the end of the actions list (create a fresh one each time)
		var closeButton = new Button();
		closeButton.Text = "Close (ESC)";
		closeButton.AddThemeFontOverride("font", _customFont);
		closeButton.AddThemeFontSizeOverride("font_size", 24);
		closeButton.CustomMinimumSize = new Vector2(0, 40);
		closeButton.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		closeButton.Pressed += OnClosePressed;
		_actionsContainer.AddChild(closeButton);

		// Position at center of screen
		var viewportSize = GetViewport().GetVisibleRect().Size;
		Position = (viewportSize - Size) / 2;

		Show();
	}

	private void OnActionPressed(string actionId)
	{
		GD.Print($"Action selected: {actionId} for NPC {_currentNpcId}");
		EmitSignal(SignalName.ActionSelected, _currentNpcId, actionId);
		Hide();
		EmitSignal(SignalName.PanelClosed);
	}

	private void OnClosePressed()
	{
		Hide();
		EmitSignal(SignalName.PanelClosed);
	}

	public override void _Input(InputEvent @event)
	{
		// Close on ESC key
		if (Visible && @event is InputEventKey keyEvent)
		{
			if (keyEvent.Pressed && keyEvent.Keycode == Key.Escape)
			{
				Hide();
				EmitSignal(SignalName.PanelClosed);
				GetViewport().SetInputAsHandled();
			}
		}
	}
}
