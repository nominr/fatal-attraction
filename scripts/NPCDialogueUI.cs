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
		// 1. Hide immediately to prevent blocking inputs if setup fails
		Visible = false;
		this.MouseFilter = MouseFilterEnum.Pass; // Allow non-handled clicks to pass

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
		_interviewOptionsContainer.AnchorRight = 1.1f; // Ends at right edge 
		
		_interviewOptionsContainer.GrowVertical = GrowDirection.Begin;
		_interviewOptionsContainer.Alignment = BoxContainer.AlignmentMode.End; // Stack items at bottom

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

		_dialogueBoxContainer = FindNodeRecursive(this, "DialogueBox");
		_nameBoxContainer = FindNodeRecursive(this, "NPCNameBox");
		
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

		if (_dialogueBoxContainer == null || _nameBoxContainer == null)
		{
			GD.PrintErr("NPCDialogueUI: Critical Nodes (DialogueBox, NPCNameBox) missing! UI will not display correctly.");
			// We DO NOT return here, to allow logic containers to exist and prevent NRE.
			// But visuals will break.
		}

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
			_npcNameLabel.AddThemeColorOverride("font_color", Colors.Black);
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
}
