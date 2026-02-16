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
	private Control _dialogueBoxContainer; // Holds Text + Standard Buttons
	private Control _nameBoxContainer;     // Holds NPC Name
	private VBoxContainer _interviewOptionsContainer; // New container for vertical interview questions
	
	// Dynamic UI Elements
	private Label _npcNameLabel;
	private RichTextLabel _dialogueTextLabel;
	private HBoxContainer _standardButtonsContainer; // HBox for standard actions
	private Label _stateLabel; // Global screen label for state vector

	
	private Font _customFont;
	private string _currentNpcId;
	private string _lastActionsHash;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always; // keep updating even if paused (optional)

		// 1. Hide immediately to prevent blocking inputs if setup fails
		Visible = false;
		this.MouseFilter = MouseFilterEnum.Pass; // Allow non-handled clicks to pass
		
		CallDeferred(nameof(EnsureHudParent));
		CallDeferred(nameof(ApplyCustomLayout));

		_customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
		
		// Create a synthetic bold variation
		var boldFont = new FontVariation();
		boldFont.BaseFont = _customFont;
		boldFont.VariationEmbolden = 1.1f; // Make it thicker

		// Initialize logic containers EARLY to prevent crashes in ShowForNPC
		// Even if visual boxes are missing, these must exist for logic to run safely (even if invisible)
		_interviewOptionsContainer = new VBoxContainer();
		_interviewOptionsContainer.Name = "InterviewOptions";
		AddChild(_interviewOptionsContainer); // Add to root initially
		
		// Configure Interview Container (Top of UI)
		_interviewOptionsContainer.SetAnchorsPreset(LayoutPreset.TopWide);
		_interviewOptionsContainer.GrowVertical = GrowDirection.Begin; // Up (Stack grows upwards from bottom anchor)
		
		// Align Bottom of container to Bottom of UI (minus name box height approx)
		_interviewOptionsContainer.AnchorTop = 0; // Can stretch up depending on content
		_interviewOptionsContainer.AnchorBottom = 1.0f; 
		_interviewOptionsContainer.OffsetBottom = -130; // Just above Name Box/Bottom Edge
		
		// Constrain width to 50% of box, starting at Center (0.5)
		_interviewOptionsContainer.AnchorLeft = 0.4f; 
		_interviewOptionsContainer.AnchorRight = 1.0f; // Ends at right edge 
		
		_interviewOptionsContainer.GrowVertical = GrowDirection.Begin;
		_interviewOptionsContainer.Alignment = BoxContainer.AlignmentMode.End; // Stack items at bottom

		// Find TextureRect (Direct child of root)
		// Check both "TextureRect" and "Background" just in case, but scene says "TextureRect"
		TextureRect texture = GetNodeOrNull<TextureRect>("TextureRect");
		
		// Fallback: Check inside Container (if scene structure changed)
		var container = GetNodeOrNull<Control>("Container");
		if (texture == null && container != null)
		{
			texture = container.GetNodeOrNull<TextureRect>("TextureRect");
		}
		
		// Restore Finding of Dialogue and Name Boxes
		if (container != null)
		{
			_dialogueBoxContainer = container.GetNodeOrNull<Control>("DialogueBox");
			_nameBoxContainer = container.GetNodeOrNull<Control>("NPCNameBox");
		}
		else
		{
			// Try recursive fallback if main container not found?
			// Or just try direct children if scene is flat
			_dialogueBoxContainer = GetNodeOrNull<Control>("DialogueBox");
			_nameBoxContainer = GetNodeOrNull<Control>("NPCNameBox");
		}
		
		// Ensure non-null to prevent crashes (though functionality will be impaired)
		if (_dialogueBoxContainer == null) GD.PrintErr("[NPCDialogueUI] DialogueBox NOT FOUND");
		if (_nameBoxContainer == null) GD.PrintErr("[NPCDialogueUI] NPCNameBox NOT FOUND");

		// --- ROOT SETUP ---
		var viewportSize = GetViewportRect().Size;
		float width = viewportSize.X;
		
		// Ensure UI appears on top
		this.ZIndex = 100;

		// --- TEXTURE SETUP ---
		if (texture != null)
		{
			string path = "res://assets/ai_interaction_panel.png";
			Texture2D newInfoPanelTexture = null;

			// Try loading as Resource (Standard)
			if (ResourceLoader.Exists(path))
			{
				newInfoPanelTexture = ResourceLoader.Load<Texture2D>(path);
			}

			// Fallback: Load as Image (if not imported yet)
			if (newInfoPanelTexture == null)
			{
				GD.Print($"[NPCDialogueUI] ResourceLoader failed for {path}. Trying direct Image load...");
				var img = new Image();
				var err = img.Load(path);
				if (err == Error.Ok)
				{
					newInfoPanelTexture = ImageTexture.CreateFromImage(img);
					GD.Print("[NPCDialogueUI] Direct Image load SUCCESS.");
				}
				else
				{
					GD.PrintErr($"[NPCDialogueUI] Direct Image load FAILED: {err}");
				}
			}

			if (newInfoPanelTexture != null)
			{
				texture.Texture = newInfoPanelTexture;
				texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
				texture.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered; 
				if (texture.GetParent() is Control p && p != this) 
				{
					p.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
				}
				
				GD.Print("[NPCDialogueUI] Texture loaded and assigned.");
			}
		}
		else
		{
			GD.PrintErr("[NPCDialogueUI] TextureRect Node NOT FOUND even after robust search! Cannot set background.");
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
		// (Rest of _Ready continues below this point automatically since we only replaced up to the Container Fix logic)


		// --- CONTENT INJECTION ---
		if (_dialogueBoxContainer != null)
		{
			foreach (Node child in _dialogueBoxContainer.GetChildren()) child.QueueFree();

			var dbContentLayout = new VBoxContainer();
			dbContentLayout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_dialogueBoxContainer.AddChild(dbContentLayout);

			// Text
			_dialogueTextLabel = new RichTextLabel();
			_dialogueTextLabel.AddThemeFontOverride("normal_font", _customFont);
			_dialogueTextLabel.AddThemeFontSizeOverride("normal_font_size", 24);
			_dialogueTextLabel.AddThemeFontOverride("bold_font", boldFont);
			_dialogueTextLabel.AddThemeFontSizeOverride("bold_font_size", 24);
			_dialogueTextLabel.AddThemeColorOverride("default_color", Colors.Black);
			_dialogueTextLabel.BbcodeEnabled = true;
			_dialogueTextLabel.SizeFlagsVertical = SizeFlags.ExpandFill;
			_dialogueTextLabel.FitContent = true; // Use FitContent to ensure all lines show if possible
			_dialogueTextLabel.ScrollActive = false; // Disable scrollbar as requested
			dbContentLayout.AddChild(_dialogueTextLabel);

			// Standard Buttons (HBox)
			_standardButtonsContainer = new HBoxContainer();
			_standardButtonsContainer.CustomMinimumSize = new Vector2(0, 40); // Reduced height to give text more space
			_standardButtonsContainer.Alignment = BoxContainer.AlignmentMode.Center;
			_standardButtonsContainer.AddThemeConstantOverride("separation", 10);
			dbContentLayout.AddChild(_standardButtonsContainer);
		}
		else
		{
			// Create dummy containers so code doesn't crash
			_standardButtonsContainer = new HBoxContainer();
			_dialogueTextLabel = new RichTextLabel(); 
		}

		if (_nameBoxContainer != null)
		{
			foreach (Node child in _nameBoxContainer.GetChildren()) child.QueueFree();
			
			_npcNameLabel = new Label();
			_npcNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
			_npcNameLabel.VerticalAlignment = VerticalAlignment.Center;
			_npcNameLabel.AddThemeFontOverride("font", _customFont);
			_npcNameLabel.AddThemeFontSizeOverride("font_size", 24);
			_npcNameLabel.AddThemeColorOverride("font_color", Colors.White); // Set to White for contrast on dark tag
			_npcNameLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			_nameBoxContainer.AddChild(_npcNameLabel);
		}
		else
		{
			_npcNameLabel = new Label();
		}

		// Initialize State Label (Top Right of Screen)
		_stateLabel = new Label();
		_stateLabel.TopLevel = true; // Independent of this control's transform
		_stateLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
		_stateLabel.Position = new Vector2(20, 20); // Top Left padding
		// Actually TopLevel anchors might refer to parent canvas. Secure way:
		// Just set it to TopRight.
		_stateLabel.AddThemeColorOverride("font_color", Colors.Black);
		_stateLabel.AddThemeFontSizeOverride("font_size", 20);
		_stateLabel.HorizontalAlignment = HorizontalAlignment.Left;
		AddChild(_stateLabel);

		_stateLabel.Visible = false;

		// Hide initially
		Visible = false;
	}

	public void ShowForNPC(string npcId, string npcName, string npcDescription, List<JToken> actions, Vector3 state)
	{
		_currentNpcId = npcId;
		_npcNameLabel.Text = npcName;

		
		// Update State Label
		if (_stateLabel != null)
		{
			_stateLabel.Text = $"NPC STATE: [A: {state.X:F1}, P: {state.Y:F1}, Pr: {state.Z:F1}]";
			// Ensure positioning (re-anchor if viewport changed)
			_stateLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft, LayoutPresetMode.KeepWidth, 20);
			// Force manual fix just in case
			_stateLabel.Position = new Vector2(20, 20);
			_stateLabel.Visible = true;
		}
		


		_dialogueTextLabel.Text = npcDescription;

		// Rebuild Buttons check
		// Since state changes frequently, include it in hash or just rebuild every time?
		// Rebuilding is safer for state display updates.
		_lastActionsHash = ""; // Force update to show new state


		// Clear containers
		foreach (Node child in _standardButtonsContainer.GetChildren()) child.QueueFree();
		foreach (Node child in _interviewOptionsContainer.GetChildren()) child.QueueFree();

		Button leaveBtn = null;

		if (actions != null)
		{
			foreach (var action in actions)
			{
				string actionId = action["id"]?.Value<string>() ?? "unknown";
				if (actionId.EndsWith("_rock") || actionId.EndsWith("_paper") || actionId.EndsWith("_scissors")) continue;

				string actionText = action["text"]?.Value<string>() ?? "Unknown";

				// CHECK FOR INTERVIEW ACTIONS (Usually stored in 'interview_data' but here we just see buttons)
				// Heuristic: If actionId starts with "ask_" or contains question mark?
				// Or if it's NOT "leave" / "interact".
				// Better: The user said "Interview buttons... vertical stack above".
				// Standard interactions: "Leave", "Talk", "Gift"??
				
				// Let's assume all actions except 'leave' are "Interaction/Interview" actions.
				// If we are in an Interview state (triggered by "start_interview"), subsequent options are interview options.
				// But we don't track state here easily.
				// However, usually interview options are long text. Standard are short.
				
				var btn = CreateActionButton(actionText, () => OnActionPressed(actionId));

				if (actionId == "leave" || actionText.ToLower().Contains("walk away"))
				{
					leaveBtn = btn;
				}
				else
				{
					// If it's a question or part of interview flow, stack vertical
					// For now, let's put ALL non-leave buttons in Vertical Stack ABOVE if there's more than 1 or if text is long?
					// Or just strictly follow: "Interview buttons" -> Vertical Above.
					// Implementation: Put all non-leave buttons in the Vertical Stack ABOVE.
					// Put only LEAVE in the horizontal row below? 
					// User said: "Standard buttons horizontal line below... INTERVIEW buttons... vertical stack above"
					
					// Let's create a visual distinction.
					// If it looks like an interview option (long text, or we are in interview mode), put above.
					// Since we can't easily know mode, let's put ALL interaction options above in the vertical stack.
					// And put "Walk Away" in the box below.
					
					// Wait, what about "Start Interview"? That's a standard button.
					// Maybe length check? 
					
					// Re-reading: "Standard buttons horizontal line below... INTERVIEW buttons vertical stack above".
					// I will put them in _interviewOptionsContainer.
					
					_interviewOptionsContainer.AddChild(btn);
					// Make them full width in the stack
					btn.CustomMinimumSize = new Vector2(0, 40); 
					btn.Alignment = HorizontalAlignment.Left;
				}
			}
		}

		// WALK AWAY / LEAVE BUTTON
		// "Rightmost button inside the dialogue box."
		// Since we put everything else in the top stack, this might be the ONLY button in the Standard Container.
		// If so, align it right?
		if (leaveBtn != null)
		{
			// Reset size/alignment for horizontal box
			leaveBtn.CustomMinimumSize = new Vector2(120, 40);
			leaveBtn.Alignment = HorizontalAlignment.Center;
			
			// If we want it rightmost, use a spacer?
			var spacer = new Control();
			spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			spacer.MouseFilter = MouseFilterEnum.Ignore;
			
			_standardButtonsContainer.AddChild(spacer); // Push button to right
			_standardButtonsContainer.AddChild(leaveBtn);
		}
		
		// Adjust visibility of containers
		// If no interview options, we might want to hide that container (it shrinks anyway).
		
		// If we put standard options (Start Interview) in the vertical stack, it might look weird.
		// "Start Interview" is usually short. 
		// Refinement: If button text length < 20, put in Standard Horizontal?
		// "Ask about the murder" (20 chars). "Start Interview" (15).
		// Let's try: If actionId starts with "ask_" or "response_", it goes vertical.
		// Else ("start_interview", "gift", etc) goes horizontal.
		
		// Re-distribute based on ID heuristic if possible, or just Move them.

		// Fallback: Just separate Leave. Code above puts everything else in vertical.
		// Let's verify results.

		Visible = true;
	}

	private Button CreateActionButton(string text, Action onPressed)
	{
		var btn = new Button();
		btn.Text = text;
		btn.AddThemeFontOverride("font", _customFont);
		btn.AddThemeFontSizeOverride("font_size", 20); // Slightly smaller for list
		btn.AutowrapMode = TextServer.AutowrapMode.WordSmart; // Enable wrapping
		btn.CustomMinimumSize = new Vector2(0, 40); // Ensure height for wrapped text
		btn.SizeFlagsHorizontal = SizeFlags.ExpandFill; // Fill valid space
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
		if (_stateLabel != null) _stateLabel.Visible = false;

		_currentNpcId = null;
		EmitSignal(SignalName.PanelClosed);
	}

	private string GenerateActionsHash(List<JToken> actions)
	{
		if (actions == null) return "null";
		return string.Join("|", actions.Select(a => a["id"]?.ToString() + a["text"]?.ToString()));
	}

	private void ApplyCustomLayout()
	{
		var vp = GetViewport().GetVisibleRect().Size;

		// Force full width using Anchors (Bottom Wide)
		// This sets Left=0, Right=1, Top=1, Bottom=1 (anchors)
		SetAnchorsPreset(LayoutPreset.BottomWide);
		
		// Reset margins to ensure we touch edges
		OffsetLeft = 0;
		OffsetRight = 0; 
		OffsetBottom = 0;

		// Compute desired height from the panel texture aspect ratio
		float aspect = 3.0f;
		var texRect =
			GetNodeOrNull<TextureRect>("TextureRect") ??
			GetNodeOrNull<TextureRect>("Container/TextureRect");

		if (texRect?.Texture != null)
		{
			var ts = texRect.Texture.GetSize();
			if (ts.X > 0 && ts.Y > 0) aspect = ts.X / ts.Y;
		}

		// Width is full viewport width (since we stretch L/R)
		float targetWidth = vp.X;
		float targetHeight = targetWidth / aspect;

		// Clamp height to reasonable limits
		// Make it at least 25% of screen, max 50%? 
		// User said "Large... proportions not squished". 
		// If aspect is wide (e.g. 5:1), height will be small. 
		// If aspect is standard (e.g. 16:9), height will be large.
		
		// Let's trust the aspect ratio primarily, but ensure a min/max
		float minHeight = 200; 
		float maxHeight = vp.Y * 0.6f;
		
		targetHeight = Mathf.Clamp(targetHeight, minHeight, maxHeight);

		// Set Height via OffsetTop (negative value from Bottom anchor)
		OffsetTop = -targetHeight;


		// Ensure children fill the resized parent
		if (texRect != null)
		{
			texRect.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			texRect.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize; 
			texRect.StretchMode = TextureRect.StretchModeEnum.Scale; 
		}

		var container = GetNodeOrNull<Control>("Container");
		if (container != null)
		{
			container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		}
		
		GD.Print($"[NPCDialogueUI] Layout Applied: Size=({targetWidth}, {targetHeight}), Pos={Position}");
	}



	private void EnsureHudParent()
	{
		// Find or create HUD CanvasLayer
		var root = GetTree().Root;
		var hud = root.GetNodeOrNull<CanvasLayer>("HUD");
		if (hud == null)
		{
			hud = new CanvasLayer();
			hud.Name = "HUD";
			root.AddChild(hud);
		}

		hud.Layer = 999;

		// Find or create a full-screen Control under the CanvasLayer
		var hudRoot = hud.GetNodeOrNull<Control>("HudRoot");
		if (hudRoot == null)
		{
			hudRoot = new Control();
			hudRoot.Name = "HudRoot";
			hudRoot.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			hudRoot.MouseFilter = MouseFilterEnum.Pass;
			hud.AddChild(hudRoot);
		}

		// If we're not already parented there, move us
		if (GetParent() != hudRoot)
		{
			var oldParent = GetParent();
			oldParent?.RemoveChild(this);
			hudRoot.AddChild(this);
		}

		SetAnchorsPreset(LayoutPreset.TopLeft);

		ZAsRelative = false;
		ZIndex = 4096;
		MouseFilter = MouseFilterEnum.Pass;

		// Make sure layout updates on resize AFTER we're under full-screen parent
		GetViewport().SizeChanged -= ApplyCustomLayout;
		GetViewport().SizeChanged += ApplyCustomLayout;

		ApplyCustomLayout(); // run immediately (not deferred)


	}


	public override void _Process(double delta) { }
}
