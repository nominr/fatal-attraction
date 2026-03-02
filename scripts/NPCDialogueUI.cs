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
	

	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel;
	
	private GridContainer _optionsGrid;
	private VBoxContainer _contentGroup; // dialogue + options wrapper
	private Control _triangleScene; // Influence triangle component (right side)
	private TextureRect _npcFaceRect; // NPC Face Display
	
	// Resources
	private Font _customFont;
	private Texture2D _panelTexture;

	private Texture2D _buttonTexture;

	// State
	private string _currentNpcId;
	private string _lastActionsHash;

	public override void _Ready()
	{
		GD.Print("[NPCDialogueUI] Initializing merged layout...");
		
		// 1. Setup Basic Properties
		TopLevel = false;
		Visible = false;
		MouseFilter = MouseFilterEnum.Pass; 

		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		_panelTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_interaction_panel.png");

		_buttonTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_npcinteraction_option.png");

		// 2. Clear existing children
		foreach (Node child in GetChildren()) child.QueueFree();

		// 3. Configure Root Layout (Bottom Wide)
		ZIndex = 10;
		SetAnchorsPreset(LayoutPreset.BottomWide, false);
		GrowVertical = GrowDirection.Begin;
		GrowHorizontal = GrowDirection.Both;
		CustomMinimumSize = new Vector2(0, 500);
		
		OffsetLeft = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		OffsetTop = -500;

		// --- MAIN BACKGROUND (Version 2 successful layout) ---
		_mainBackground = new TextureRect();
		_mainBackground.Texture = _panelTexture;
		_mainBackground.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_mainBackground.StretchMode = TextureRect.StretchModeEnum.Scale;
		_mainBackground.SetAnchorsPreset(LayoutPreset.FullRect);
		
		_mainBackground.OffsetLeft = -92;   
		_mainBackground.OffsetRight = 108;  
		_mainBackground.OffsetBottom = 285; 
		_mainBackground.OffsetTop = 0;

		_mainBackground.SelfModulate = Colors.White;
		_mainBackground.MouseFilter = MouseFilterEnum.Ignore;
		AddChild(_mainBackground);

		// --- CENTRAL CONTAINER (Version 2 successful layout) ---
		_centralContainer = new VBoxContainer();
		_centralContainer.SetAnchorsPreset(LayoutPreset.FullRect);
		_centralContainer.Alignment = BoxContainer.AlignmentMode.End; // push content toward bottom
		_centralContainer.AddThemeConstantOverride("separation", 8);
		_centralContainer.GrowHorizontal = GrowDirection.Both;
		_centralContainer.GrowVertical = GrowDirection.Both;

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 450);  // narrower box
		margin.AddThemeConstantOverride("margin_right", 450); // narrower box
		margin.AddThemeConstantOverride("margin_top", 40);
		margin.AddThemeConstantOverride("margin_bottom", 160); // reserve space for option buttons
		AddChild(margin);
		margin.AddChild(_centralContainer);

		// --- NPC FACE ---
		_npcFaceRect = new TextureRect();
		_npcFaceRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_npcFaceRect.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_npcFaceRect.CustomMinimumSize = new Vector2(180, 225);
		_npcFaceRect.SetAnchorsPreset(LayoutPreset.BottomLeft);
		_npcFaceRect.Position = new Vector2(40, -233);
		_npcFaceRect.ZIndex = 11;
		AddChild(_npcFaceRect);

		// --- NPC NAME (fixed, independent position) ---
		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Left;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 30);
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.White);
		_npcNameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_npcNameLabel.AddThemeConstantOverride("outline_size", 2);
		_npcNameLabel.ZIndex = 15;
		_npcNameLabel.AnchorLeft   = 0.5f;
		_npcNameLabel.AnchorRight  = 0.5f;
		_npcNameLabel.AnchorTop    = 1.0f;
		_npcNameLabel.AnchorBottom = 1.0f;
		_npcNameLabel.GrowHorizontal = GrowDirection.Both;
		_npcNameLabel.GrowVertical   = GrowDirection.Begin;
		_npcNameLabel.OffsetLeft   = -319;
		_npcNameLabel.OffsetRight  =  319;
		_npcNameLabel.OffsetBottom = -235; // fixed — never tied to dialogue or options
		AddChild(_npcNameLabel);

		// contentGroup spans the fixed zone from y=280 (guide line) to y=480 (near panel bottom).
		// This guarantees the dialogue box NEVER goes above the magenta boundary.
		_contentGroup = new VBoxContainer();
		_contentGroup.AddThemeConstantOverride("separation", 12);
		_contentGroup.ZIndex = 11;
		_contentGroup.ClipContents = true; // hard boundary — nothing leaks above y=280
		_contentGroup.MouseFilter = MouseFilterEnum.Ignore;
		_contentGroup.Alignment = BoxContainer.AlignmentMode.End; // content stacks from bottom
		_contentGroup.AnchorLeft   = 0.5f;
		_contentGroup.AnchorRight  = 0.5f;
		_contentGroup.AnchorTop    = 0f;   // relative to root top
		_contentGroup.AnchorBottom = 1f;   // relative to root bottom
		_contentGroup.GrowHorizontal = GrowDirection.Both;
		_contentGroup.OffsetLeft   = -319;
		_contentGroup.OffsetRight  =  319;
		_contentGroup.OffsetTop    = 280;  // hard top = guide line
		_contentGroup.OffsetBottom = -20;  // near panel bottom
		AddChild(_contentGroup);

		// Visual guide line — marks the top boundary the dialogue box must not cross.
		// Bright so you can see it and tell us if it needs to move.
		var guideLine = new ColorRect();
		guideLine.Color = new Color(1f, 0f, 1f, 0f); // magenta @ y=280, kept for reference, invisible
		guideLine.CustomMinimumSize = new Vector2(0, 2);
		guideLine.SetAnchorsPreset(LayoutPreset.TopWide);
		guideLine.OffsetTop  = 280;
		guideLine.OffsetBottom = 282;
		guideLine.MouseFilter = MouseFilterEnum.Ignore;
		guideLine.ZIndex = 20;
		AddChild(guideLine);

		var contentGroup = _contentGroup;

		// Dialogue panel — true pixel cube corners via NinePatch StyleBoxTexture
		var dialoguePanel = new PanelContainer();
		dialoguePanel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		dialoguePanel.MouseFilter = MouseFilterEnum.Ignore;
		dialoguePanel.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		dialoguePanel.AddThemeStyleboxOverride("panel",
			CreatePixelBorderStyle(new Color("#25273e"), new Color("#3a3d5c"), 24, 24, 8, 8));
		contentGroup.AddChild(dialoguePanel);

		var boldFont = new FontVariation();
		boldFont.BaseFont = _customFont;
		boldFont.VariationEmbolden = 1.1f;

		_dialogueTextLabel = new RichTextLabel();
		_dialogueTextLabel.BbcodeEnabled = true;
		_dialogueTextLabel.FitContent = true;
		_dialogueTextLabel.ScrollActive = false;
		_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
		_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 28);
		_dialogueTextLabel.AddThemeFontOverride("bold_font", boldFont);
		_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", 28);
		_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.White);
		_dialogueTextLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_dialogueTextLabel.HorizontalAlignment = HorizontalAlignment.Center;
		dialoguePanel.AddChild(_dialogueTextLabel);

		// Options grid (bottom sibling — always below dialogue, 3px gap enforced by VBox)
		_optionsGrid = new GridContainer();
		_optionsGrid.Columns = 2;
		_optionsGrid.AddThemeConstantOverride("h_separation", 9);
		_optionsGrid.AddThemeConstantOverride("v_separation", 4);
		_optionsGrid.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		contentGroup.AddChild(_optionsGrid);

	}

	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions, Vector3 state, Texture2D npcFace = null)
	{
		_currentNpcId = npcId;
		Visible = true;
		
		if (npcFace != null)
		{
			_npcFaceRect.Texture = npcFace;
			_npcFaceRect.Visible = true;
		}
		else
		{
			_npcFaceRect.Visible = false;
		}
		
		// Pulse the layout
		SetAnchorsPreset(LayoutPreset.BottomWide, false);
		OffsetLeft = 0;
		OffsetRight = 0;
		OffsetBottom = 0;
		OffsetTop = -500;
		
		_npcNameLabel.Text = npcName.ToUpper();
		
		// Dynamic Font Scaling for Dialogue
		int dialogueFontSize = 30;
		if (npcDescription.Length > 200) dialogueFontSize = 20;
		else if (npcDescription.Length > 80) dialogueFontSize = 24;
		
		_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", dialogueFontSize);
		_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", dialogueFontSize);
		_dialogueTextLabel.Text = npcDescription;

		// Switch layout: centered when no options (post-game response), bottom-aligned otherwise
		bool hasActions = actions != null && actions.Count > 0;
		if (!hasActions)
		{
			// Extend to full bottom and center — places box midway between guide line and screen bottom
			_contentGroup.OffsetBottom = 0;
			_contentGroup.Alignment = BoxContainer.AlignmentMode.Center;
			_npcNameLabel.Visible = false;
		}
		else
		{
			// Restore normal bottom margin and stack content from the bottom up
			_contentGroup.OffsetBottom = -20;
			_contentGroup.Alignment = BoxContainer.AlignmentMode.End;
			_npcNameLabel.Visible = true;
		}
		

		string currentHash = npcId + "|" + npcDescription + "|" + GenerateActionsHash(actions);
		if (currentHash == _lastActionsHash) { return; }
		_lastActionsHash = currentHash;

		foreach (Node child in _optionsGrid.GetChildren()) child.QueueFree();

		if (actions != null)
		{
			var visibleActions = actions.ToList();
			
			_optionsGrid.Columns = visibleActions.Count <= 2 ? 2 : 2;
			
			int zIndexCounter = 100;
			foreach (var action in visibleActions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				string actionText = action["text"]?.Value<string>() ?? "Option";

				var btnWrapper = CreateStyledButton(actionText, () => OnActionPressed(actionId));
				btnWrapper.ZIndex = zIndexCounter--; 
				_optionsGrid.AddChild(btnWrapper);
			}

			// Keep 4 slots for layout consistency if needed
			for (int i = visibleActions.Count; i < 4; i++)
			{
				var spacer = new Control();
				spacer.CustomMinimumSize = new Vector2(315, 50); // Prevent row collapse
				spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				spacer.SizeFlagsVertical = SizeFlags.ExpandFill;
				_optionsGrid.AddChild(spacer);
			}
		}
	}

	private Control CreateStyledButton(string text, Action onPressed)
	{
		// Merged Implementation (Version 1 Style)
		var quadrant = new PanelContainer();
		quadrant.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		quadrant.SizeFlagsVertical = SizeFlags.ExpandFill;
		quadrant.CustomMinimumSize = new Vector2(315, 50); // Scaled down 10% from 350x55
		
		// Pixel cube corner style via NinePatch texture
		var pixelStyle = CreatePixelBorderStyle(
			new Color(0.88f, 0.87f, 0.84f, 0.95f),
			new Color("#3a3d5c"), // match dialogue box border
			8, 8, 4, 4);
		quadrant.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;
		quadrant.AddThemeStyleboxOverride("panel", pixelStyle);

		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		
		// Font scaling
		int fontSize = 32;
		if (text.Length > 40) fontSize = 22;
		else if (text.Length > 25) fontSize = 26;
		
		btn.AddThemeFontSizeOverride("font_size", fontSize);
		btn.AddThemeColorOverride("font_color", Colors.Black);
		btn.AddThemeColorOverride("font_hover_color", Colors.DarkGray);
		btn.AddThemeColorOverride("font_pressed_color", Colors.Black);
		
		btn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		
		var emptyStyle = new StyleBoxEmpty();
		btn.AddThemeStyleboxOverride("normal", emptyStyle);
		btn.AddThemeStyleboxOverride("hover", emptyStyle);
		btn.AddThemeStyleboxOverride("pressed", emptyStyle);
		btn.AddThemeStyleboxOverride("focus", emptyStyle); 
		
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		btn.SizeFlagsVertical = SizeFlags.ExpandFill;
		btn.Pressed += onPressed;
		
		btn.MouseEntered += () => quadrant.Modulate = new Color(0.9f, 0.9f, 0.9f);
		btn.MouseExited += () => quadrant.Modulate = new Color(1, 1, 1);
		btn.ButtonDown += () => quadrant.Modulate = new Color(0.75f, 0.75f, 0.75f);
		btn.ButtonUp += () => quadrant.Modulate = new Color(0.9f, 0.9f, 0.9f);

		quadrant.AddChild(btn);
		return quadrant;
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

	/// <summary>
	/// Builds a StyleBoxTexture using a 9×9 NinePatch where the 3×3 corner regions are
	/// fully transparent — this produces hard square pixel notches at every corner.
	/// </summary>
	private StyleBoxTexture CreatePixelBorderStyle(
		Color fill, Color border,
		int contentL, int contentR, int contentT, int contentB)
	{
		const int b = 3; // border + corner size in pixels
		const int s = 9; // total image size (3 * 3)

		var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
		for (int y = 0; y < s; y++)
		for (int x = 0; x < s; x++)
		{
			bool isCorner = (x < b || x >= s - b) && (y < b || y >= s - b);
			bool isFill   = x >= b && x < s - b && y >= b && y < s - b;
			img.SetPixel(x, y, isCorner ? Colors.Transparent : isFill ? fill : border);
		}

		var tex   = ImageTexture.CreateFromImage(img);
		var style = new StyleBoxTexture();
		style.Texture = tex;
		style.SetTextureMargin(Side.Left,   b);
		style.SetTextureMargin(Side.Right,  b);
		style.SetTextureMargin(Side.Top,    b);
		style.SetTextureMargin(Side.Bottom, b);
		style.ContentMarginLeft   = contentL;
		style.ContentMarginRight  = contentR;
		style.ContentMarginTop    = contentT;
		style.ContentMarginBottom = contentB;
		return style;
	}
}
