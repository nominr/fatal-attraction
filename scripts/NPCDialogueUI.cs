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

	// UI Elements
	private TextureRect _mainBackground;
	private VBoxContainer _centralContainer;
	
	private TextureRect _dialogueBackground;
	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel;
	
	private GridContainer _optionsGrid;
	private Label _stateLabel; // Global screen label available to debug
	private Control _triangleScene; // Influence triangle component (right side)
	private TextureRect _npcFaceRect; // NPC Face Display
	
	// Resources
	private Font _customFont;
	private Texture2D _panelTexture;
	private Texture2D _dialogueTexture;
	private Texture2D _buttonTexture;

	// State
	private string _currentNpcId;
	private string _lastActionsHash;

	public override void _Ready()
	{
		GD.Print("CRITICAL DEBUG: NPCDialogueUI _Ready called (This script is ACTIVE)");
		// 1. Setup Basic Properties
		TopLevel = false; // Changed to false so it respects CanvasLayer (UI layer)
		Visible = false;
		MouseFilter = MouseFilterEnum.Pass; 

		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		_panelTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_interaction_panel.png");
		_dialogueTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_npcinteraction_dialogue.png");
		_buttonTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_npcinteraction_option.png");

		// 2. Clear existing children to build fresh UI
		foreach (Node child in GetChildren()) child.QueueFree();

		// 3. Configure Root Layout (Bottom Wide, Full Width)
		ZIndex = 10;
		
		// Use LayoutPreset.BottomWide to automatically set anchors to (0,1) for left/right and (1,1) for bottom
		// keep_offsets=false to RESET offsets to 0, ensuring it snaps to edges
		SetAnchorsPreset(LayoutPreset.BottomWide, false);
		
		// Grow Direction: Up (Begin) so that height extends upwards from bottom
		GrowVertical = GrowDirection.Begin;
		GrowHorizontal = GrowDirection.Both;

		// SIZE: Enforce height
		CustomMinimumSize = new Vector2(0, 500); // 500px height
		
		// Reset offsets to ensure it sticks to edges
		OffsetLeft = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		OffsetTop = -500; // Extend upwards by 500px


		GD.Print($"[NPCDialogueUI] _Ready Layout: AnchorLeft={AnchorLeft}, AnchorRight={AnchorRight}, Size={Size}, Viewport={GetViewportRect().Size}");
		
		// --- MAIN BACKGROUND ---
		// The background texture likely has transparent margins, so we OVERSCAN it.
		// We set negative offsets to stretch it BEYOND the actual control bounds.
		_mainBackground = new TextureRect();
		_mainBackground.Texture = _panelTexture;
		_mainBackground.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mainBackground.StretchMode = TextureRect.StretchModeEnum.Scale;
		
		// Use FullRect anchors...
		_mainBackground.SetAnchorsPreset(LayoutPreset.FullRect);
		
		// ...BUT apply offsets to stretch outwards
		// Left/Top/Right/Bottom relative to anchors
		// User feedback: 
		// "overcompensated" right shift -> Move back left slightly.
		// "few pixels lower" -> Increase bottom offset slightly.
		// Previous Right Shift: +15 (Left -85, Right 115).
		// Previous Bottom: 250.
		// New Goal: Shift +8, Bottom 320.
		// User feedback (Step 197): "A few pixels higher" -> Decrease bottom offset.
		// 320 was too low. 250 was too high ("lower").
		// Let's try 285.
		_mainBackground.OffsetLeft = -92;   
		_mainBackground.OffsetRight = 108;  
		_mainBackground.OffsetBottom = 285; 
		_mainBackground.OffsetTop = 0;     // Keep top aligned for now

		_mainBackground.SelfModulate = Colors.White;
		_mainBackground.Visible = true;
		_mainBackground.MouseFilter = MouseFilterEnum.Ignore; // Let clicks pass through to buttons below
		AddChild(_mainBackground);
		// ...

		
		// --- CENTRAL CONTAINER (VBox) ---
		// CRITICAL: This must be a sibling of _mainBackground, NOT a child
		// TextureRect cannot properly parent container nodes
		_centralContainer = new VBoxContainer();
		_centralContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		_centralContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_centralContainer.AddThemeConstantOverride("separation", 15); // Tighter separation
		_centralContainer.GrowHorizontal = GrowDirection.Both;
		_centralContainer.GrowVertical = GrowDirection.Both;
		// Add padding to keep content away from panel edges
		// MASSIVE margins - Triangle on RIGHT, NPC face on LEFT
		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 300); // Space for NPC face on left (Increased for even less width)
		margin.AddThemeConstantOverride("margin_right", 300); // Space for triangle on right (Increased for even less width)
		margin.AddThemeConstantOverride("margin_top", 60);   // Decrease top margin to move HIGHER
		margin.AddThemeConstantOverride("margin_bottom", 10);
		AddChild(margin);
		margin.AddChild(_centralContainer);


		margin.AddChild(_centralContainer);


		// --- NPC FACE (Bottom Left) ---
		_npcFaceRect = new TextureRect();
		_npcFaceRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize; // Allow manual sizing
		_npcFaceRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_npcFaceRect.CustomMinimumSize = new Vector2(180, 225); // Smaller size (approx 72%)
		_npcFaceRect.SetAnchorsPreset(LayoutPreset.BottomLeft);
		_npcFaceRect.Position = new Vector2(40, -233); // Adjusted to maintain bottom padding (-8px relative to bottom)
		_npcFaceRect.ZIndex = 11; // On top of background but below options if needed
		
		// Add to main background or root? Root is safer for positioning independent of margins
		AddChild(_npcFaceRect);


		// --- DIALOGUE SECTION ---
		// Use TextureRect directly for background with content overlaid
		var dialogueContainer = new Control();
		dialogueContainer.CustomMinimumSize = new Vector2(0, 450); // Taller dialogue box (Increased further)
		dialogueContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill; // Fill available width
		_centralContainer.AddChild(dialogueContainer);
		
		// Dialogue background texture
		_dialogueBackground = new TextureRect();
		_dialogueBackground.Texture = _dialogueTexture;
		_dialogueBackground.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_dialogueBackground.StretchMode = TextureRect.StretchModeEnum.Scale;
		_dialogueBackground.SetAnchorsPreset(LayoutPreset.FullRect);
		_dialogueBackground.SelfModulate = Colors.White;
		_dialogueBackground.MouseFilter = MouseFilterEnum.Ignore;
		dialogueContainer.AddChild(_dialogueBackground);

		// Content on top of background
		var dialogueContent = new VBoxContainer();
		dialogueContent.SetAnchorsPreset(LayoutPreset.FullRect);
		dialogueContent.Alignment = BoxContainer.AlignmentMode.Center;
		dialogueContent.AddThemeConstantOverride("separation", 50); // Add significant separation to push TEXT down below Name
		
		var dialogueMargin = new MarginContainer();
		dialogueMargin.SetAnchorsPreset(LayoutPreset.FullRect);
		dialogueMargin.AddThemeConstantOverride("margin_left", 100);
		dialogueMargin.AddThemeConstantOverride("margin_right", 100);
		dialogueMargin.AddThemeConstantOverride("margin_top", 10); // Reduced top margin to move Name HIGHER
		dialogueMargin.AddThemeConstantOverride("margin_bottom", 10);
		
		dialogueContainer.AddChild(dialogueMargin);
		dialogueMargin.AddChild(dialogueContent);

		// NPC Name - Positioned at Top Left within VBox
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Left; // Align Left
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 35);
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.White);
		_npcNameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_npcNameLabel.AddThemeConstantOverride("outline_size", 2);
		dialogueContent.AddChild(_npcNameLabel); // Add back to VBox for reliable rendering
		
		// Create a synthetic bold variation
		var boldFont = new FontVariation();
		boldFont.BaseFont = _customFont;
		boldFont.VariationEmbolden = 1.1f; // Make it thicker
		
		// Dialogue Text
		_dialogueTextLabel = new RichTextLabel();
		_dialogueTextLabel.BbcodeEnabled = true; // Enable BBCode for bold/formatting
		_dialogueTextLabel.FitContent = true;
		_dialogueTextLabel.ScrollActive = false;
		_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
		_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 28);
		_dialogueTextLabel.AddThemeFontOverride("bold_font", boldFont); // Add bold font
		_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", 28);
		_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.White);
		_dialogueTextLabel.CustomMinimumSize = new Vector2(0, 0); // Removed fixed width
		_dialogueTextLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill; // Fill available width
		dialogueContent.AddChild(_dialogueTextLabel);
		

		// Initialize logic containers EARLY to prevent crashes in ShowForNPC
		// Even if visual boxes are missing, these must exist for logic to run safely (even if invisible)
		// _interviewOptionsContainer = new VBoxContainer(); // This was removed as it's not used and causes issues
		// _interviewOptionsContainer.Name = "InterviewOptions";
		// AddChild(_interviewOptionsContainer); // Add to root initially
		
		// Configure Interview Container (Top of UI)
		// _interviewOptionsContainer.SetAnchorsPreset(LayoutPreset.TopWide);
		// _interviewOptionsContainer.GrowVertical = GrowDirection.Begin; // Up (Stack grows upwards from bottom anchor)
		
		// Align Bottom of container to Bottom of UI (minus name box height approx)
		// _interviewOptionsContainer.AnchorTop = 0; // Can stretch up depending on content
		// _interviewOptionsContainer.AnchorBottom = 1.0f; 
		// _interviewOptionsContainer.OffsetBottom = -130; // Just above Name Box/Bottom Edge
		
		// Constrain width to 50% of box, starting at Center (0.5)
		// _interviewOptionsContainer.AnchorLeft = 0.4f; 
		// _interviewOptionsContainer.AnchorRight = 1.1f; // Ends at right edge 
		
		// _interviewOptionsContainer.GrowVertical = GrowDirection.Begin;
		// _interviewOptionsContainer.Alignment = BoxContainer.AlignmentMode.End; // Stack items at bottom

		// --- FIND NODES ROBUSTLY (Recursive) ---
		Control FindNodeRecursive(Node parent, string name)
		{
			if (parent == null) return null;
			var child = parent.GetNodeOrNull<Control>(name);
			if (child != null) return child;
			foreach (Node kid in parent.GetChildren())
			{
				var res = FindNodeRecursive(kid, name);
				if (res != null) return res;
			}
			return null;
		}

		// _dialogueBoxContainer = FindNodeRecursive(this, "DialogueBox"); // Removed as it's not used and causes issues
		// _nameBoxContainer = FindNodeRecursive(this, "NPCNameBox"); // Removed as it's not used and causes issues
		
		// Find TextureRect (might be named TextureRect or just be a TextureRect)
		TextureRect texture = null;
		var container = GetNodeOrNull<Control>("Container");
		if (container != null) 
		{
			container.MouseFilter = MouseFilterEnum.Pass; 
			texture = container.GetNodeOrNull<TextureRect>("TextureRect");
		}
		
		if (texture == null)
		{
			// Search recursively for ANY TextureRect if specific name not found?
			// Or just search by name "TextureRect"
			var possibleTexture = FindNodeRecursive(this, "TextureRect");
			if (possibleTexture is TextureRect tr) texture = tr;
		}

		// if (_dialogueBoxContainer == null || _nameBoxContainer == null) // Removed as it's not used and causes issues
		// {
		// 	GD.PrintErr("NPCDialogueUI: Critical Nodes (DialogueBox, NPCNameBox) missing! UI will not display correctly.");
		// 	// We DO NOT return here, to allow logic containers to exist and prevent NRE.
		// 	// But visuals will break.
		// }

		// --- ROOT SETUP ---
		var viewportSize = GetViewportRect().Size;
		float width = viewportSize.X * 0.33f;
		float height = viewportSize.Y * 0.25f;

		this.SetAnchorsPreset(LayoutPreset.CenterBottom);
		this.Size = new Vector2(width, height);
		this.Position = new Vector2((viewportSize.X - width) / 2, viewportSize.Y - height - 40);

		// --- TEXTURE SETUP ---
		if (texture != null)
		{
			texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
			texture.StretchMode = TextureRect.StretchModeEnum.Scale;
			// Ensure it fills its parent (likely Container or Root) to act as background
			texture.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			if (texture.GetParent() is Control p && p != this) 
			{
				// If texture is inside a container, make sure that container fills our Root
				p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			}
		}
		
		// --- CONTAINER LAYOUT FIX ---
		// The 'Container' holding the boxes might be tiny (pixel art size). 
		// We must force it to fill the root so the anchors on boxes work relative to the big UI.
		if (container != null)
		{
			container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			// Also ensure it doesn't block mouse
			container.MouseFilter = MouseFilterEnum.Pass;
		}

		// --- CONTENT INJECTION ---
		// if (_dialogueBoxContainer != null) // Removed as it's not used and causes issues
		// {
		// 	foreach (Node child in _dialogueBoxContainer.GetChildren()) child.QueueFree();

		// 	var dbContentLayout = new VBoxContainer();
		// 	dbContentLayout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		// 	_dialogueBoxContainer.AddChild(dbContentLayout);

		// 	// Text
		// 	_dialogueTextLabel = new RichTextLabel();
		// 	_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
		// 	_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 24);
		// 	_dialogueTextLabel.AddThemeFontOverride("bold_font", boldFont);
		// 	_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", 24);
		// 	_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.Black);
		// 	_dialogueTextLabel.BbcodeEnabled = true;
		// 	_dialogueTextLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
		// 	_dialogueTextLabel.FitContent = true; // Use FitContent to ensure all lines show if possible
		// 	_dialogueTextLabel.ScrollActive = false; // Disable scrollbar as requested
		// 	dbContentLayout.AddChild(_dialogueTextLabel);
		// }


		// --- OPTIONS SECTION ---
		// Buttons in 2x2 grid layout
		// Remove from central container to position independently at bottom
		_optionsGrid = new GridContainer();
		_optionsGrid.Columns = 2;
		_optionsGrid.AddThemeConstantOverride("h_separation", -40); // Horizontal spacing (Significantly Reduced/Negative)
		_optionsGrid.AddThemeConstantOverride("v_separation", -170); // Vertical spacing (Significantly Reduced/Negative)
		
		AddChild(_optionsGrid); // Add to root control
		_optionsGrid.ZIndex = 1; // Ensure it renders on top of everything
		
		// MANUAL ANCHORING - Set exactly to Center Bottom
		_optionsGrid.AnchorLeft = 0.5f;
		_optionsGrid.AnchorRight = 0.5f;
		_optionsGrid.AnchorTop = 1.0f;
		_optionsGrid.AnchorBottom = 1.0f;
		_optionsGrid.GrowHorizontal = GrowDirection.Both; // Expand outwards from center
		_optionsGrid.GrowVertical = GrowDirection.Begin; // Expand upwards from bottom
		
		// Offsets - relative to center-bottom anchor
		// Shift Left (Reset from 50)
		_optionsGrid.OffsetLeft = 0; 
		_optionsGrid.OffsetRight = 0; 
		
		// Align to bottom edge safely (Shifted DOWN by 80px per user request)
		// GrowVertical = Begin ensures it grows UP from this bottom offset
		_optionsGrid.OffsetBottom = 40;  
		// removed hardcoded OffsetTop to allow auto-sizing


		// --- STATE LABEL (Debug) ---
		_stateLabel = new Label();
		_stateLabel.TopLevel = true; // Independent of this control's transform
		_stateLabel.SetAnchorsPreset(LayoutPreset.TopLeft);
		_stateLabel.Position = new Vector2(20, 20);
		_stateLabel.AddThemeColorOverride("font_color", Colors.White);
		_stateLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_stateLabel.AddThemeConstantOverride("outline_size", 4);
		_stateLabel.Visible = false;
		AddChild(_stateLabel);
	}

	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions, Vector3 state, Texture2D npcFace = null)
	{
		_currentNpcId = npcId;
		Visible = true;
		
		// Set Face
		if (npcFace != null)
		{
			_npcFaceRect.Texture = npcFace;
			_npcFaceRect.Visible = true;
		}
		else
		{
			_npcFaceRect.Visible = false;
		}
		
		// FORCE LAYOUT UPDATE
		// Re-apply anchors to ensure it sticks to bottom
		SetAnchorsPreset(LayoutPreset.BottomWide, false);
		OffsetLeft = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		OffsetTop = -500;
		
		GD.Print($"[NPCDialogueUI] ShowForNPC: Pos={Position}, Size={Size}, Viewport={GetViewportRect().Size}");
		
		// Update Text
		_npcNameLabel.Text = npcName.ToUpper();
		_dialogueTextLabel.Text = npcDescription;
		
		// Debug State
		if (_stateLabel != null)
		{
			_stateLabel.Text = $"NPC STATE: [A: {state.X:F1}, P: {state.Y:F1}, Pr: {state.Z:F1}]";
			_stateLabel.Visible = true;
		}
		


		_dialogueTextLabel.Text = npcDescription;


		// --- HASH CHECK TO PREVENT FLICKER ---
		// We only rebuild buttons if the actions or description text changes.
		// We do NOT rebuild just because the vector state changed.
		string currentHash = npcId + "|" + npcDescription + "|" + GenerateActionsHash(actions);

		if (currentHash == _lastActionsHash)
		{
			Visible = true;
			return; // EXIT EARLY - NO REBUILD
		}
		
		_lastActionsHash = currentHash;


	// Note: leaveBtn was originally used but the value is never read
	// Button leaveBtn = null;

		// Rebuild Buttons
		foreach (Node child in _optionsGrid.GetChildren()) child.QueueFree();

		if (actions != null)
		{
			var filteredActions = actions.Where(action => 
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				return !actionId.EndsWith("_rock") && !actionId.EndsWith("_paper") && !actionId.EndsWith("_scissors");
			}).ToList();
			
			// Adjust grid layout based on button count
			// 2 buttons = 1x2 (side by side), 3-4 buttons = 2x2 grid
			_optionsGrid.Columns = filteredActions.Count <= 2 ? 2 : 2;
			
			int zIndexCounter = 100; // Counter to invert Z-Index so TOP buttons overlay BOTTOM ones
			foreach (var action in filteredActions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				string actionText = action["text"]?.Value<string>() ?? "Option";

				var btn = CreateStyledButton(actionText, () => OnActionPressed(actionId));
				// CRITICAL FIX: Higher Z-Index for FIRST buttons prevents lower buttons from blocking clicks
				btn.ZIndex = zIndexCounter--; 
				_optionsGrid.AddChild(btn);
			}
		}
	}

	private Button CreateStyledButton(string text, Action onPressed)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		btn.AddThemeFontSizeOverride("font_size", 32); // Larger font (32)
		btn.AddThemeColorOverride("font_color", Colors.Black);
		btn.AddThemeColorOverride("font_hover_color", Colors.DarkGray);
		btn.AddThemeColorOverride("font_pressed_color", Colors.Black);
		
		// Apply Texture Style
		var normStyle = new StyleBoxTexture { Texture = _buttonTexture };
		// Adjust content margins to ensure text doesn't hit edges of button art
		normStyle.ContentMarginLeft = 20;
		normStyle.ContentMarginRight = 20;
		normStyle.ContentMarginTop = 15; // Equal to bottom for vertical centering
		normStyle.ContentMarginBottom = 15; // Equal to top for vertical centering

		var hoverStyle = new StyleBoxTexture { Texture = _buttonTexture, ModulateColor = new Color(0.9f, 0.9f, 0.9f) };
		hoverStyle.ContentMarginLeft = 20; hoverStyle.ContentMarginRight = 20;
		hoverStyle.ContentMarginTop = 15; hoverStyle.ContentMarginBottom = 15;

		var pressStyle = new StyleBoxTexture { Texture = _buttonTexture, ModulateColor = new Color(0.7f, 0.7f, 0.7f) };
		pressStyle.ContentMarginLeft = 20; pressStyle.ContentMarginRight = 20;
		pressStyle.ContentMarginTop = 17; pressStyle.ContentMarginBottom = 13; // Slight shift down effect (2px difference)

		btn.AddThemeStyleboxOverride("normal", normStyle);
		btn.AddThemeStyleboxOverride("hover", hoverStyle);
		btn.AddThemeStyleboxOverride("pressed", pressStyle);
		btn.AddThemeStyleboxOverride("focus", new StyleBoxEmpty()); // Remove focus ring
		
		// Button size - revert to large stacked size (350x250)
		btn.CustomMinimumSize = new Vector2(350, 230); // Much larger dimensions
		btn.SizeFlagsHorizontal = SizeFlags.ShrinkCenter; // Don't expand to fill, stay centered
		btn.SizeFlagsVertical = SizeFlags.ShrinkCenter; // Don't expand vertically

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
		if (_stateLabel != null) _stateLabel.Visible = false;
		EmitSignal(SignalName.PanelClosed);
	}

	// Generate a hash of the actions list to detect changes
	private string GenerateActionsHash(List<JToken> actions)
	{
		if (actions == null || actions.Count == 0) return "";
		
		var hashParts = new List<string>();
		foreach (var action in actions)
		{
			string actionId = action["id"]?.Value<string>() ?? "";
			string actionText = action["text"]?.Value<string>() ?? "";
			hashParts.Add($"{actionId}:{actionText}");
		}
		
		return string.Join("|", hashParts);
	}
}
