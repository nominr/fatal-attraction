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
		GD.Print("[NPCDialogueUI] Initializing merged layout...");
		
		// 1. Setup Basic Properties
		TopLevel = false;
		Visible = false;
		MouseFilter = MouseFilterEnum.Pass; 

		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		_panelTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_interaction_panel.png");
		_dialogueTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_npcinteraction_dialogue.png");
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
		_centralContainer.Alignment = BoxContainer.AlignmentMode.Center;
		_centralContainer.AddThemeConstantOverride("separation", 15);
		_centralContainer.GrowHorizontal = GrowDirection.Both;
		_centralContainer.GrowVertical = GrowDirection.Both;

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_left", 300);
		margin.AddThemeConstantOverride("margin_right", 300);
		margin.AddThemeConstantOverride("margin_top", 60);
		margin.AddThemeConstantOverride("margin_bottom", 10);
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

		// --- DIALOGUE SECTION ---
		var dialogueContainer = new Control();
		dialogueContainer.CustomMinimumSize = new Vector2(0, 380); // Reduced height from 450
		dialogueContainer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_centralContainer.AddChild(dialogueContainer);
		
		_dialogueBackground = new TextureRect();
		_dialogueBackground.Texture = _dialogueTexture;
		_dialogueBackground.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		_dialogueBackground.StretchMode = TextureRect.StretchModeEnum.Scale;
		_dialogueBackground.SetAnchorsPreset(LayoutPreset.FullRect);
		_dialogueBackground.MouseFilter = MouseFilterEnum.Ignore;
		dialogueContainer.AddChild(_dialogueBackground);

		var dialogueContent = new VBoxContainer();
		dialogueContent.SetAnchorsPreset(LayoutPreset.FullRect);
		dialogueContent.Alignment = BoxContainer.AlignmentMode.Center;
		dialogueContent.AddThemeConstantOverride("separation", 20); // Reduced from 50
		
		var dialogueMargin = new MarginContainer();
		dialogueMargin.SetAnchorsPreset(LayoutPreset.FullRect);
		dialogueMargin.AddThemeConstantOverride("margin_left", 100);
		dialogueMargin.AddThemeConstantOverride("margin_right", 100);
		dialogueMargin.AddThemeConstantOverride("margin_top", 10);
		dialogueMargin.AddThemeConstantOverride("margin_bottom", 10);
		dialogueContainer.AddChild(dialogueMargin);
		dialogueMargin.AddChild(dialogueContent);

		_npcNameLabel = new Label();
		_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Left;
		_npcNameLabel.AddThemeFontOverride("font", _customFont);
		_npcNameLabel.AddThemeFontSizeOverride("font_size", 30); // Reduced from 35
		_npcNameLabel.AddThemeColorOverride("font_color", Colors.White);
		_npcNameLabel.AddThemeColorOverride("font_outline_color", Colors.Black);
		_npcNameLabel.AddThemeConstantOverride("outline_size", 2);
		dialogueContent.AddChild(_npcNameLabel);
		
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
		dialogueContent.AddChild(_dialogueTextLabel);

		// --- OPTIONS SECTION ---
		_optionsGrid = new GridContainer();
		_optionsGrid.Columns = 2;
		_optionsGrid.AddThemeConstantOverride("h_separation", 9);
		_optionsGrid.AddThemeConstantOverride("v_separation", 4);
		
		AddChild(_optionsGrid);
		_optionsGrid.ZIndex = 12; // Above background
		
		_optionsGrid.AnchorLeft = 0.5f;
		_optionsGrid.AnchorRight = 0.5f;
		_optionsGrid.AnchorTop = 1.0f;
		_optionsGrid.AnchorBottom = 1.0f;
		_optionsGrid.GrowHorizontal = GrowDirection.Both;
		_optionsGrid.GrowVertical = GrowDirection.Begin;
		
		_optionsGrid.OffsetLeft = 0; 
		_optionsGrid.OffsetRight = 0; 
		_optionsGrid.OffsetBottom = -30;  // Moved down 5px from -35

		// Debug Label
		_stateLabel = new Label();
		_stateLabel.TopLevel = true;
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
		int dialogueFontSize = 22; // Reduced base size from 28
		if (npcDescription.Length > 200) dialogueFontSize = 16;
		else if (npcDescription.Length > 80) dialogueFontSize = 19;
		
		_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", dialogueFontSize);
		_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", dialogueFontSize);
		_dialogueTextLabel.Text = npcDescription;
		
		if (_stateLabel != null)
		{
			_stateLabel.Text = $"NPC STATE: [A: {state.X:F1}, P: {state.Y:F1}, Pr: {state.Z:F1}]";
			_stateLabel.Visible = true;
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
		
		var pixelStyle = new StyleBoxFlat();
		pixelStyle.BgColor = new Color(0.88f, 0.87f, 0.84f, 0.95f);
		pixelStyle.BorderColor = new Color(0.12f, 0.1f, 0.18f, 1.0f);
		pixelStyle.SetBorderWidthAll(3);
		pixelStyle.SetCornerRadiusAll(0);
		pixelStyle.ShadowColor = new Color(0.35f, 0.33f, 0.4f, 0.8f);
		pixelStyle.ShadowSize = 2;
		pixelStyle.ShadowOffset = Vector2.Zero;
		pixelStyle.ContentMarginLeft = 8;
		pixelStyle.ContentMarginRight = 8;
		pixelStyle.ContentMarginTop = 4;
		pixelStyle.ContentMarginBottom = 4;
		quadrant.AddThemeStyleboxOverride("panel", pixelStyle);

		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		
		// Font scaling
		int fontSize = 24;
		if (text.Length > 40) fontSize = 16;
		else if (text.Length > 25) fontSize = 19;
		
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
		if (_stateLabel != null) _stateLabel.Visible = false;
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
}
