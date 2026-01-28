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
	private Label _npcDescLabel;
	private ColorRect _portraitRect;
	private VBoxContainer _actionsContainer;
	private Button _closeButton;

	// Current state
	private string _currentNpcId;
	private Font _customFont;
	private string _lastContentSignature = "";

	public override void _Ready()
	{
		SetupUI();
		Hide(); // Hidden by default
	}

	private void SetupUI()
	{
		// Load custom font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		// Panel styling
		CustomMinimumSize = new Vector2(600, 300);

		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(marginContainer);

		// Main HBox: Portrait on left, Dialogue on right (Stardew Valley style)
		var mainHBox = new HBoxContainer();
		mainHBox.AddThemeConstantOverride("separation", 20);
		marginContainer.AddChild(mainHBox);

		// LEFT SIDE: Portrait Area
		var portraitVBox = new VBoxContainer();
		portraitVBox.AddThemeConstantOverride("separation", 10);
		portraitVBox.CustomMinimumSize = new Vector2(120, 0);
		mainHBox.AddChild(portraitVBox);

		// Portrait placeholder (colored circle for now)
		var portraitPanel = new PanelContainer();
		portraitPanel.CustomMinimumSize = new Vector2(100, 100);
		portraitVBox.AddChild(portraitPanel);
		
		var portraitRect = new ColorRect();
		portraitRect.CustomMinimumSize = new Vector2(100, 100);
		portraitRect.Color = Colors.Gray;
		portraitRect.MouseFilter = Control.MouseFilterEnum.Ignore; // Don't block
		portraitPanel.AddChild(portraitRect);
		_portraitRect = portraitRect;

		// NPC name below portrait
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 18);
		_npcNameLabel.MouseFilter = Control.MouseFilterEnum.Ignore; // Don't block
		portraitVBox.AddChild(_npcNameLabel);

		// RIGHT SIDE: Dialogue and Actions
		var dialogueVBox = new VBoxContainer();
		dialogueVBox.AddThemeConstantOverride("separation", 15);
		dialogueVBox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		mainHBox.AddChild(dialogueVBox);

		// NPC dialogue text
		_npcDescLabel = new Label();
		_npcDescLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		_npcDescLabel.CustomMinimumSize = new Vector2(0, 80);
		_npcDescLabel.AddThemeFontOverride("font", _customFont);
		_npcDescLabel.AddThemeFontSizeOverride("font_size", 16);
		_npcDescLabel.MouseFilter = Control.MouseFilterEnum.Ignore; // Don't block
		dialogueVBox.AddChild(_npcDescLabel);

		// Separator
		var separator = new HSeparator();
		separator.MouseFilter = Control.MouseFilterEnum.Ignore;
		dialogueVBox.AddChild(separator);

		// Actions label
		var actionsLabel = new Label();
		actionsLabel.Text = "What will you do?";
		actionsLabel.AddThemeFontOverride("font", _customFont);
		actionsLabel.AddThemeFontSizeOverride("font_size", 14);
		actionsLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
		dialogueVBox.AddChild(actionsLabel);

		// Scroll container for actions
		// Scroll container for actions
		var scrollContainer = new ScrollContainer();
		scrollContainer.CustomMinimumSize = new Vector2(0, 100);
		scrollContainer.SizeFlagsVertical = SizeFlags.ExpandFill;
		scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; // Prevent horizontal scrolling
		scrollContainer.MouseFilter = Control.MouseFilterEnum.Pass; // Allow clicks to pass
		dialogueVBox.AddChild(scrollContainer);

		_actionsContainer = new VBoxContainer();
		_actionsContainer.AddThemeConstantOverride("separation", 8);
		_actionsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill; // Ensure full width
		_actionsContainer.MouseFilter = Control.MouseFilterEnum.Pass; // Allow clicks to pass
		scrollContainer.AddChild(_actionsContainer);

		// Close button
		_closeButton = new Button();
		_closeButton.Text = "Close (ESC)";
		_closeButton.AddThemeFontOverride("font", _customFont);
		_closeButton.Pressed += OnClosePressed;
		dialogueVBox.AddChild(_closeButton);

		// Layering and Positioning
		ZIndex = 95; // High ZIndex to sit above most UI (Producer panels are ~0, GameOver is 99)
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
		GrowHorizontal = Control.GrowDirection.Both;
		GrowVertical = Control.GrowDirection.Both;
	}

	/// <summary>
	/// Show the interaction panel for a specific NPC with available actions
	/// </summary>
	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions)
	{
		// Generate Content Signature to prevent unnecessary rebuilds (which cause flickering)
		var actionIds = new List<string>();
		if (actions != null)
		{
			foreach (var a in actions) 
				actionIds.Add(a["id"]?.Value<string>() ?? "null");
		}
		string newSignature = $"{npcId}|{npcDescription}|{string.Join(",", actionIds)}";

		// If nothing changed and panel is already visible, just return
		if (Visible && _lastContentSignature == newSignature)
		{
			return;
		}

		_lastContentSignature = newSignature;
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;
		_npcDescLabel.Text = npcDescription;

		// Set portrait color based on NPC ID
		_portraitRect.Color = npcId.ToLower() switch
		{
			"katy" => Colors.DeepPink,
			"john" => Colors.DodgerBlue,
			"rebecca" => Colors.Orange,
			"marcus" => Colors.LimeGreen,
			"sofia" => Colors.Orchid,
			_ => Colors.Gray
		};

		// Clear previous actions
		foreach (Node child in _actionsContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Create action buttons
		if (actions == null || actions.Count == 0)
		{
			var noActionsLabel = new Label();
			noActionsLabel.Text = "No actions available for this NPC.";
			noActionsLabel.HorizontalAlignment = HorizontalAlignment.Center;
			noActionsLabel.AddThemeFontOverride("font", _customFont);
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
				actionButton.CustomMinimumSize = new Vector2(0, 40); // Taller buttons for easier clicking
				actionButton.AddThemeFontOverride("font", _customFont);
				actionButton.AddThemeFontSizeOverride("font_size", 20); // Larger text
				
				// Capture the actionId for the lambda
				string capturedActionId = actionId;
				actionButton.Pressed += () => OnActionPressed(capturedActionId);
				
				_actionsContainer.AddChild(actionButton);
			}
		}

		// Reset position to center (force update)
		SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);

		Show();
	}

	private void OnActionPressed(string actionId)
	{
		GD.Print($"Action selected: {actionId} for NPC {_currentNpcId}");
		EmitSignal(SignalName.ActionSelected, _currentNpcId, actionId);
		
		// Don't close panel if it's an interview step
		// "start_interview" or "interview_option_..."
		if (!actionId.Contains("interview"))
		{
			Hide();
			EmitSignal(SignalName.PanelClosed);
		}
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
