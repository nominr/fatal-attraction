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
	private VBoxContainer _actionsContainer;
	private Button _closeButton;

	// Current state
	private string _currentNpcId;

	public override void _Ready()
	{
		SetupUI();
		Hide(); // Hidden by default
	}

	private void SetupUI()
	{
		// Panel styling
		CustomMinimumSize = new Vector2(400, 300);

		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		AddChild(marginContainer);

		var mainVBox = new VBoxContainer();
		mainVBox.AddThemeConstantOverride("separation", 15);
		marginContainer.AddChild(mainVBox);

		// Header with NPC name
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 24);
		mainVBox.AddChild(_npcNameLabel);

		// NPC description
		_npcDescLabel = new Label();
		_npcDescLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcDescLabel.AutowrapMode = TextServer.AutowrapMode.Word;
		mainVBox.AddChild(_npcDescLabel);

		// Separator
		var separator = new HSeparator();
		mainVBox.AddChild(separator);

		// Actions label
		var actionsLabel = new Label();
		actionsLabel.Text = "Available Actions:";
		mainVBox.AddChild(actionsLabel);

		// Scroll container for actions (in case there are many)
		var scrollContainer = new ScrollContainer();
		scrollContainer.CustomMinimumSize = new Vector2(0, 150);
		mainVBox.AddChild(scrollContainer);

		_actionsContainer = new VBoxContainer();
		_actionsContainer.AddThemeConstantOverride("separation", 8);
		scrollContainer.AddChild(_actionsContainer);

		// Close button
		_closeButton = new Button();
		_closeButton.Text = "Close";
		_closeButton.Pressed += OnClosePressed;
		mainVBox.AddChild(_closeButton);
	}

	/// <summary>
	/// Show the interaction panel for a specific NPC with available actions
	/// </summary>
	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions)
	{
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;
		_npcDescLabel.Text = npcDescription;

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
			_actionsContainer.AddChild(noActionsLabel);
		}
		else
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				string actionText = action["text"]?.Value<string>() ?? "Unknown Action";

				var actionButton = new Button();
				actionButton.Text = actionText;
				actionButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				
				// Capture the actionId for the lambda
				string capturedActionId = actionId;
				actionButton.Pressed += () => OnActionPressed(capturedActionId);
				
				_actionsContainer.AddChild(actionButton);
			}
		}

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
