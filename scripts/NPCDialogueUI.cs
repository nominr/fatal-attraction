using Godot;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

/// <summary>
/// Controls the NPC Dialogue Interact UI.
/// Attached to the root of npc_dialogue_scene.tscn.
/// </summary>
public partial class NPCDialogueUI : Control
{
	[Signal]
	public delegate void ActionSelectedEventHandler(string npcId, string actionId);

	[Signal]
	public delegate void PanelClosedEventHandler();

	// Scene References
	private Control _dialogueBoxContainer; // specific logic container inside the visual asset
	private Control _nameBoxContainer;     // specific logic container inside the visual asset
	
	// Dynamic UI Elements
	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel; // Using RichTextLabel for better wrapping
	private HBoxContainer _actionsContainer;
	
	private Font _customFont;
	private string _currentNpcId;

	public override void _Ready()
	{
		// Load Font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");

		// Locate Containers based on scene structure:
		// NPCDialogueScene (Root) -> DialogueBoxContainer -> NPCDialogueBoxAsset -> DialogueBox / NPCNameBox
		_dialogueBoxContainer = GetNodeOrNull<Control>("DialogueBoxContainer/NPCDialogueBoxAsset/DialogueBox");
		_nameBoxContainer = GetNodeOrNull<Control>("DialogueBoxContainer/NPCDialogueBoxAsset/NPCNameBox");

		if (_dialogueBoxContainer == null || _nameBoxContainer == null)
		{
			GD.PrintErr("NPCDialogueUI: Could not find required containers in scene tree.");
			return;
		}

		// Setup Name Label
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcNameLabel.VerticalAlignment = VerticalAlignment.Center;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 28);
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.Black); // Assuming light bg, or adjust as needed
		_nameBoxContainer.AddChild(_npcNameLabel);
		// Make label fill the container
		_npcNameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Hide by default
		Visible = false;
	}

	/// <summary>
	/// Display the dialogue UI for a specific NPC.
	/// </summary>
	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions)
	{
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;

		// Clear previous content in DialogueBox
		foreach (Node child in _dialogueBoxContainer.GetChildren())
		{
			child.QueueFree();
		}

		// Rebuild Layout
		// 1. VBox for vertical stacking (Text top, buttons bottom)
		var mainVBox = new VBoxContainer();
		mainVBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_dialogueBoxContainer.AddChild(mainVBox);

		// 2. Dialogue Text (Top, Expands)
		_dialogueTextLabel = new RichTextLabel();
		_dialogueTextLabel.Text = npcDescription;
		_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
		_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 24);
		_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.Black); // Assuming light bg
		_dialogueTextLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
		_dialogueTextLabel.FitContent = true; // Use fit content primarily? Or ExpandFill?
		// Actually, standard VBox expansion is better for fixed area.
		mainVBox.AddChild(_dialogueTextLabel);

		// 3. Spacer (Optional, VBox handles it)
		
		// 4. Action Buttons HBox (Bottom)
		_actionsContainer = new HBoxContainer();
		_actionsContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_actionsContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_actionsContainer.AddThemeConstantOverride("separation", 20);
		mainVBox.AddChild(_actionsContainer);

		// Add Action Buttons
		if (actions != null)
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				// Filter RPS
				if (actionId.EndsWith("_rock") || actionId.EndsWith("_paper") || actionId.EndsWith("_scissors")) continue;

				string actionText = action["text"]?.Value<string>() ?? "Unknown";

				var btn = CreateActionButton(actionText, () => OnActionPressed(actionId));
				_actionsContainer.AddChild(btn);
			}
		}

		// Add "Walk Away" button (Always present, per request: Bottom Right corner)
		// Since we are in an HBox, putting it at the end works visually as "rightmost".
		// But user requested "In the bottom right corner". 
		// If we want it structurally distinct from the "row", we might need a separate container.
		// However, "row at the bottom" + "walk away in bottom right" sounds like the row IS at the bottom, and walk away is the last item.
		// Let's add it to the HBox for now.
		var closeBtn = CreateActionButton("Walk Away", OnClosePressed);
		_actionsContainer.AddChild(closeBtn);

		Visible = true;
	}

	private Button CreateActionButton(string text, Action onPressed)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		btn.AddThemeFontSizeOverride("font_size", 20);
		btn.Pressed += onPressed;
		return btn;
	}

	private void OnActionPressed(string actionId)
	{
		EmitSignal(SignalName.ActionSelected, _currentNpcId, actionId);
		Close();
	}

	private void OnClosePressed()
	{
		Close();
	}

	private void Close()
	{
		Visible = false;
		EmitSignal(SignalName.PanelClosed);
	}
}
