using Godot;
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;

public partial class NPCDialogueUI : Control
{
	[Signal]
	public delegate void ActionSelectedEventHandler(string npcId, string actionId);

	[Signal]
	public delegate void PanelClosedEventHandler();

	// Scene References
	private Control _assetRoot;
	private Control _dialogueBoxContainer; // Holds Text + Buttons
	private Control _nameBoxContainer;     // Holds NPC Name
	
	// Dynamic UI Elements
	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel;
	private Control _buttonsContainer; // wrapper for button layout
	
	private Font _customFont;
	private string _currentNpcId;
	private string _lastActionsHash;

	public override void _Ready()
	{
		// Load Font
		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");

		// Locate Containers based on scene structure
		// NPCDialogueBoxScene root -> NPCDialogueUI (this)
		// Children: DialogueBox, NPCNameBox, TextureRect (Background)
		
		_dialogueBoxContainer = GetNodeOrNull<Control>("DialogueBox");
		_nameBoxContainer = GetNodeOrNull<Control>("NPCNameBox");
		_assetRoot = this;

		if (_dialogueBoxContainer == null || _nameBoxContainer == null)
		{
			// Try looking for NPCDialogueBoxAsset wrapper just in case
			_dialogueBoxContainer = GetNodeOrNull<Control>("NPCDialogueBoxAsset/DialogueBox");
			_nameBoxContainer = GetNodeOrNull<Control>("NPCDialogueBoxAsset/NPCNameBox");
			_assetRoot = GetNodeOrNull<Control>("NPCDialogueBoxAsset") ?? this;
		}

		if (_dialogueBoxContainer == null || _nameBoxContainer == null)
		{
			GD.PrintErr("NPCDialogueUI: Required containers (DialogueBox, NPCNameBox) not found!");
			return; 
		}

		// --- PROGRAMMATIC LAYOUT FORCE ---
		// 1. Set Size: 60% Width, 25% Height (Smaller than before)
		var viewportSize = GetViewportRect().Size;
		float width = viewportSize.X * 0.6f; 
		float height = viewportSize.Y * 0.25f;
		
		_assetRoot.SetAnchorsPreset(LayoutPreset.CenterBottom);
		_assetRoot.Size = new Vector2(width, height);
		// Position: Center Bottom with padding
		_assetRoot.Position = new Vector2((viewportSize.X - width) / 2, viewportSize.Y - height - 40);
		_assetRoot.Scale = Vector2.One; // Reset any scaling

		// 2. Force Background to Fill
		var texture = _assetRoot.GetNodeOrNull<TextureRect>("TextureRect") ?? _assetRoot.GetNodeOrNull<TextureRect>("NPCDialogueBoxAsset/TextureRect");
		if (texture != null)
		{
			texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			texture.StretchMode = TextureRect.StretchModeEnum.Tile; 
		}

		// 3. Name Box: Top Left floating above
		_nameBoxContainer.SetAnchorsPreset(LayoutPreset.TopLeft);
		_nameBoxContainer.Position = new Vector2(0, -40); // Move UP outside the box
		_nameBoxContainer.Size = new Vector2(250, 40);
		
		// 4. Dialogue Box: Fill the remaining space
		_dialogueBoxContainer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		_dialogueBoxContainer.OffsetLeft = 30;
		_dialogueBoxContainer.OffsetRight = -30;
		_dialogueBoxContainer.OffsetTop = 20;
		_dialogueBoxContainer.OffsetBottom = -20;

		// --- CONTENT SETUP ---
		
		// Setup Name Label in Name Box
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_npcNameLabel.VerticalAlignment = VerticalAlignment.Center;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 24);
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.Black);
		// Force background for Name Box if not present in scene?
		// Assuming scene has style. If not, add one?
		// Let's add a stylebox just in case user scene is empty container
		var nameStyle = new StyleBoxFlat();
		nameStyle.BgColor = Colors.White;
		nameStyle.SetCornerRadiusAll(5);
		// _npcNameLabel.AddThemeStyleboxOverride("normal", nameStyle); // Only if we want label to have bg. container usually has it.
		
		_nameBoxContainer.AddChild(_npcNameLabel);
		_npcNameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

		// Hide initially
		Visible = false;
	}

	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions)
	{
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;
		
		// Ensure Content Containers exist
		if (_dialogueTextLabel == null || _dialogueTextLabel.GetParent() != _dialogueBoxContainer)
		{
			// Clear logic children (keep visual children if any? no, logic controls content)
			foreach (Node child in _dialogueBoxContainer.GetChildren()) child.QueueFree();

			// Layout: VBox (Text Top, Buttons Bottom)
			var mainVBox = new VBoxContainer();
			_dialogueBoxContainer.AddChild(mainVBox);
			mainVBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			
			// Text Area
			_dialogueTextLabel = new RichTextLabel();
			_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
			_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 24);
			_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.Black);
			_dialogueTextLabel.SizeFlagsVertical = SizeFlags.ExpandFill; // Fill available vertical space
			_dialogueTextLabel.FitContent = false; // Scroll if too long? Or clipp.
			
			mainVBox.AddChild(_dialogueTextLabel);
			
			// Spacer
			var spacer = new Control();
			spacer.CustomMinimumSize = new Vector2(0, 10);
			mainVBox.AddChild(spacer);

			// Buttons Area (Bottom)
			_buttonsContainer = new Control();
			_buttonsContainer.CustomMinimumSize = new Vector2(0, 50); // Height for button row
			_buttonsContainer.SizeFlagsVertical = SizeFlags.ShrinkEnd;
			mainVBox.AddChild(_buttonsContainer);
		}
		
		_dialogueTextLabel.Text = npcDescription;

		// Rebuild Buttons
		// Check hash
		string currentHash = GenerateActionsHash(actions);
		if (_lastActionsHash == currentHash)
		{
			Visible = true;
			return;
		}
		_lastActionsHash = currentHash;

		foreach (Node child in _buttonsContainer.GetChildren()) child.QueueFree();

		// We need a HBox for centered buttons, and separate logic for "Leave"
		var buttonRow = new HBoxContainer();
		buttonRow.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		buttonRow.Alignment = BoxContainer.AlignmentMode.Center;
		buttonRow.AddThemeConstantOverride("separation", 20);
		_buttonsContainer.AddChild(buttonRow);

		Button leaveBtn = null;

		if (actions != null)
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				// Filter RPS
				if (actionId.EndsWith("_rock") || actionId.EndsWith("_paper") || actionId.EndsWith("_scissors")) continue;

				string actionText = action["text"]?.Value<string>() ?? "Unknown";
				
				var btn = CreateActionButton(actionText, () => OnActionPressed(actionId));

				// Check for Leave/Walk Away
				if (actionId == "leave" || actionText.ToLower().Contains("walk away"))
				{
					leaveBtn = btn;
				}
				else
				{
					buttonRow.AddChild(btn);
				}
			}
		}

		// Place Leave Button at Bottom Right corner of the Container
		if (leaveBtn != null)
		{
			// We add it to _buttonsContainer directly, ABOVE the HBox
			// But HBox fills rect.
			// Button needs to be On Top visually.
			// Add it to _buttonsContainer
			// But wait, HBox is child.
			// Add LeaveBtn to _buttonsContainer (as sibling of HBox)
			// HBox handles layout for centered buttons.
			// LeaveBtn handles its own layout (Anchors Bottom Right)
			
			// Warning: HBox might take up space/clicks? 
			// HBox is default mouse filter usually stop/pass.
			// Make HBox allow clicks through?
			// Actually, buttons inside HBox capture clicks. Empty space passes.
			
			_buttonsContainer.AddChild(leaveBtn);
			leaveBtn.SetAnchorsPreset(LayoutPreset.BottomRight);
			leaveBtn.GrowHorizontal = GrowDirection.Begin; // Left
			leaveBtn.OffsetRight = 0;
			leaveBtn.OffsetBottom = 0;
		}

		Visible = true;
	}

	private Button CreateActionButton(string text, Action onPressed)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		btn.AddThemeFontSizeOverride("font_size", 24);
		btn.CustomMinimumSize = new Vector2(120, 40);
		btn.Pressed += onPressed;
		return btn;
	}

	private void OnActionPressed(string actionId)
	{
		EmitSignal(SignalName.ActionSelected, _currentNpcId, actionId);
	}
	
	public void Close()
	{
		Visible = false;
		_currentNpcId = null;
		EmitSignal(SignalName.PanelClosed);
	}

	private string GenerateActionsHash(List<JToken> actions)
	{
		if (actions == null) return "null";
		return string.Join("|", actions.Select(a => a["id"]?.ToString() + a["text"]?.ToString()));
	}
}
