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
	// Scene References
	private Control _dialogueBoxContainer; // specific logic container inside the visual asset
	private Control _nameBoxContainer;     // specific logic container inside the visual asset
	
	// Dynamic UI Elements
	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel; // Using RichTextLabel for better wrapping
	private VBoxContainer _actionsContainer;
	
	private Font _customFont;
	private string _currentNpcId;
	private string _lastActionsHash; // To prevent rebuilding if same

	public override void _Ready()
	{
		// Load Font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");

		// Locate Containers based on scene structure:
		// NPCDialogueScene (Root) -> NPCDialogueBoxAsset -> DialogueBox / NPCNameBox
		_dialogueBoxContainer = GetNodeOrNull<Control>("NPCDialogueBoxAsset/DialogueBox");
		_nameBoxContainer = GetNodeOrNull<Control>("NPCDialogueBoxAsset/NPCNameBox");

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
		
		// Update Description
		// Check if we need to rebuild text?
		// Text label is created dynamically inside _dialogueBoxContainer. 
		// If we want to avoid clearing it, we should keep a ref.
		// For now, let's just clear/rebuild text, but keep buttons if possible?
		// Actually, if we clear _dialogueBoxContainer, we lose the text.
		
		// Let's restructure:
		// _dialogueTextLabel should be permanent or reused?
		// The previous implementation cleared ALL children of _dialogueBoxContainer.
		// That causes the flash/rebuild.
		
		// 1. Setup Dialogue Text (Create once if missing)
		if (_dialogueTextLabel == null || _dialogueTextLabel.GetParent() != _dialogueBoxContainer)
		{
			// Clear old junk
			foreach (Node child in _dialogueBoxContainer.GetChildren()) child.QueueFree();
			
			_dialogueTextLabel = new RichTextLabel();
			_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
			_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 24);
			_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.Black);
			_dialogueTextLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
			_dialogueTextLabel.FitContent = true;
			_dialogueBoxContainer.AddChild(_dialogueTextLabel);
		}
		
		if (_dialogueTextLabel.Text != npcDescription)
		{
			_dialogueTextLabel.Text = npcDescription;
		}

		// 2. Setup Buttons Container (VBox, Top Right)
		// We want this container OUTSIDE the DialogueBox (text area).
		// Parent: NPCDialogueBoxAsset (TextureRect).
		var assetRoot = _dialogueBoxContainer.GetParent() as Control;
		if (assetRoot == null) return;

		if (_actionsContainer == null || _actionsContainer.GetParent() != assetRoot)
		{
			if (_actionsContainer != null) _actionsContainer.QueueFree();
			
			_actionsContainer = new VBoxContainer();
			_actionsContainer.Alignment = BoxContainer.AlignmentMode.End; // Bottom-to-top stacking visually if growing up? 
			// No, VBox stacks Top-to-Bottom.
			// If we want "stacked on top", we want the bottom-most button to be near the box.
			// So Anchor Bottom-Right of the container to Top-Right of the box?
			// Let's set anchors to Top-Right of parent.
			
			assetRoot.AddChild(_actionsContainer);
			
			// Position: Top Right corner of the Asset
			_actionsContainer.SetAnchorsPreset(LayoutPreset.TopRight);
			_actionsContainer.GrowVertical = GrowDirection.Begin; // Grow Upwards
			_actionsContainer.Position = new Vector2(assetRoot.Size.X, 0) + new Vector2(0, -10); // Offset slightly up?
			// Wait, SetAnchorsPreset might reset position.
			// Let's just use anchors.
			_actionsContainer.AnchorLeft = 1;
			_actionsContainer.AnchorTop = 0;
			_actionsContainer.AnchorRight = 1;
			_actionsContainer.AnchorBottom = 0;
			_actionsContainer.OffsetLeft = -350; // Max width approx 25 chars * 14px = 350
			_actionsContainer.OffsetTop = -400; // Allow enough height for stack
			_actionsContainer.OffsetRight = 0;  // Flush right
			_actionsContainer.OffsetBottom = 20; // Lowered: overlaps/close to top of box (+20 relative to corner?)
			// Wait, parent is top-right of box? No, parent is AssetRoot (TextureRect).
			// If Anchor Top is 0, +20 is INSIDE the box.
			// Currently box is at bottom of screen.
			// AssetRoot is the dialogue box texture.
			// Anchor (1,0) is Top Right corner of the TEXTURE.
			// Offsets (0, 20) puts it 20px DOWN from top edge (Inside).
			// User said "slightly lower".
			// Previous was `-10` (10px ABOVE top edge).
			// If I set it to `20`, it's 20px inside.
			// "DIRECTLY above/stacked ontop" -> "appear slightly lower" -> maybe just touching?
			// I'll try `0` (flush with top) or `10`.
			// Let's use `0`.
			// Also checking button width in CreateActionButton.
			
			// Grow from bottom-up? VBox lays out Top-Down.
			// If we want them close to the box, we should use Alignment = End (Bottom).
			_actionsContainer.Alignment = BoxContainer.AlignmentMode.End;
			_actionsContainer.AddThemeConstantOverride("separation", 10);
		}

		// 3. Rebuild Actions ONLY if changed
		string currentHash = GenerateActionsHash(actions);
		if (_lastActionsHash == currentHash)
		{
			Visible = true;
			return; // No need to rebuild buttons
		}
		_lastActionsHash = currentHash;

		// Clear old buttons in the container
		foreach (Node child in _actionsContainer.GetChildren())
		{
			child.QueueFree();
		}

		if (actions != null)
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				if (actionId.EndsWith("_rock") || actionId.EndsWith("_paper") || actionId.EndsWith("_scissors")) continue;

				string actionText = action["text"]?.Value<string>() ?? "Unknown";
				
				// Prepend "> " style? User didn't ask, but common for vertical lists.
				var btn = CreateActionButton(actionText, () => OnActionPressed(actionId));
				btn.Alignment = HorizontalAlignment.Right; // Align text to right?
				_actionsContainer.AddChild(btn);
			}
		}

		// "Walk Away" button - Add to top or bottom?
		// "Stacked ontop" implies part of the stack.
		// User said "Directly above/stacked ontop".
		var closeBtn = CreateActionButton("Walk Away", OnClosePressed);
		closeBtn.Alignment = HorizontalAlignment.Right;
		_actionsContainer.AddChild(closeBtn);

		Visible = true;
	}

	private string GenerateActionsHash(List<JToken> actions)
	{
		if (actions == null) return "null";
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		foreach(var a in actions)
		{
			sb.Append(a["id"]?.ToString());
			sb.Append("|");
			sb.Append(a["text"]?.ToString());
			sb.Append("||");
		}
		return sb.ToString();
	}

	private Button CreateActionButton(string text, Action onPressed)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		btn.AddThemeFontSizeOverride("font_size", 24);
		btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		btn.CustomMinimumSize = new Vector2(350, 0); // Fixed width, flexible height
		btn.SizeFlagsHorizontal = SizeFlags.ShrinkEnd; // Keep aligned to right
		btn.Alignment = HorizontalAlignment.Right; // Text alignment matches
		btn.Pressed += onPressed;
		return btn;
	}

	private void OnActionPressed(string actionId)
	{
		//Reset hash to force rebuild next time if needed, or keeping it is fine.
		EmitSignal(SignalName.ActionSelected, _currentNpcId, actionId);
		// Do NOT close immediately? Or do?
		// User: "dialogue box should STAY OPEN until the interview is over"
		// If action triggers interview end, it will close.
		// If action continues interview, it stays open.
		// For normal interactions, usually clicking an action closes the menu or advances.
		// "buttons only work after clicking several times" -> fixed by hash check.
		
		// Standard behavior: Close on action?
		// The user complained about it closing "PREMATURELY" (distance check).
		// If I select "Ask Question", I expect the menu to stay/refresh?
		// Logic:
		// If the server replies, it updates the "actions" list? 
		// If so, hash changes -> rebuild.
		// If I close here, it might flash closed/open.
		// Better to NOT close here, and let the server response (or distance check) handle state.
		// BUT if it's a one-shot interaction (like "Steal Item"), we want it to close.
		
		// I'll keep Close() but maybe only if NOT interviewing?
		// Or assume the server will send a "Close" signal or empty actions?
		// For safety, I'll comment out Close() here and rely on game logic provided by GameWorld updates.
		// Actually, GameWorld.OnActionSelected just key sends to server.
		// If I don't close, the menu persists.
		// Should I close?
		// User: "STAY OPEN until the interview is over"
		// This implies for interviews it should stay open.
		// For others?
		// I will removed Close() call here.
		// Close(); 
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
