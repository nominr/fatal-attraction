using Godot;
using System;

/// <summary>
/// Semi-transparent black overlay that displays after rock, paper, scissors is played.
/// Shows the player's hand and the NPC's hand for one second.
/// </summary>
public partial class RPSResultOverlay : CanvasLayer
{
	private ColorRect _background;
	private TextureRect _playerHand;
	private TextureRect _npcHand;
	private float _displayTime = 1.5f;
	private float _elapsedTime = 0.0f;
	private bool _isDisplaying = false;
	
	// Animation properties
	private Tween _animationTween;
	private float _animationDuration = 0.5f;
	
	// Final positions for the hands
	private Vector2 _playerHandFinalPosition;
	private Vector2 _npcHandFinalPosition;

	public override void _Ready()
	{
		// Set layer to exactly cover the screen
		Layer = 10;
		
		// Create the background overlay
		_background = new ColorRect();
		_background.Color = new Color(0, 0, 0, 0.5f); // Semi-transparent black
		_background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_background.AnchorLeft = 0;
		_background.AnchorTop = 0;
		_background.AnchorRight = 1;
		_background.AnchorBottom = 1;
		_background.OffsetLeft = 0;
		_background.OffsetTop = 0;
		_background.OffsetRight = 0;
		_background.OffsetBottom = 0;
		_background.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_background);
		
		// Create player hand display (right side)
		_playerHand = new TextureRect();
		// Use Control positioning instead of anchors for easier animation
		_playerHand.CustomMinimumSize = new Vector2(200, 200);
		_playerHand.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_playerHand.MouseFilter = Control.MouseFilterEnum.Ignore;
		_playerHand.PivotOffset = new Vector2(100, 100); // Center pivot for rotation
		AddChild(_playerHand);
		
		// Calculate final position for player hand (right side)
		var viewportSize = GetViewport().GetVisibleRect().Size;
		_playerHandFinalPosition = new Vector2(viewportSize.X * 0.65f - 100, viewportSize.Y * 0.3f - 100);

		// Create NPC hand display (left side)
		_npcHand = new TextureRect();
		// Use Control positioning instead of anchors for easier animation
		_npcHand.CustomMinimumSize = new Vector2(200, 200);
		_npcHand.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_npcHand.MouseFilter = Control.MouseFilterEnum.Ignore;
		_npcHand.PivotOffset = new Vector2(100, 100); // Center pivot for rotation
		AddChild(_npcHand);
		
		// Calculate final position for NPC hand (left side)
		_npcHandFinalPosition = new Vector2(viewportSize.X * 0.15f - 100, viewportSize.Y * 0.3f - 100);
		
		// Start hidden
		Visible = false;
		_isDisplaying = false;
	}

	public override void _Process(double delta)
	{
		if (!_isDisplaying)
			return;

		_elapsedTime += (float)delta;

		if (_elapsedTime >= _displayTime)
		{
			// Hide the overlay after the display time
			Visible = false;
			_isDisplaying = false;
			_elapsedTime = 0.0f;
		}
	}

	/// <summary>
	/// Shows the overlay for one second with the player's chosen move.
	/// The NPC's hand will be determined to beat or lose to the player's choice.
	/// </summary>
	public void Show(string playerMove, bool playerWins = false)
	{
		Visible = true;
		_isDisplaying = true;
		_elapsedTime = 0.0f;

		GD.Print($"[RPSResultOverlay] Showing overlay with player move: {playerMove}, Player wins: {playerWins}");

		// Load player's hand texture (player version)
		string playerHandPath = GetPlayerHandAsset(playerMove.ToLower());
		GD.Print($"[RPSResultOverlay] Loading player hand: {playerHandPath}");
		if (ResourceLoader.Exists(playerHandPath))
		{
			_playerHand.Texture = ResourceLoader.Load<Texture2D>(playerHandPath);
			_playerHand.Visible = true;
			_playerHand.ZIndex = 100;
			GD.Print($"[RPSResultOverlay] Player hand loaded");
		}
		else
		{
			GD.PrintErr($"[RPSResultOverlay] Player hand texture not found: {playerHandPath}");
		}

		// Determine and load NPC's hand (NPC version)
		string npcMove = DetermineNPCMove(playerMove, playerWins);
		string npcHandPath = GetNPCHandAsset(npcMove.ToLower());
		GD.Print($"[RPSResultOverlay] Loading NPC hand: {npcHandPath}");
		if (ResourceLoader.Exists(npcHandPath))
		{
			_npcHand.Texture = ResourceLoader.Load<Texture2D>(npcHandPath);
			_npcHand.Visible = true;
			_npcHand.ZIndex = 100;
			GD.Print($"[RPSResultOverlay] NPC hand loaded");
		}
		else
		{
			GD.PrintErr($"[RPSResultOverlay] NPC hand texture not found: {npcHandPath}");
		}
		
		// Animate hands spawning from top corners
		AnimateHandsSpawn();
	}
	
	/// <summary>
	/// Animates the hands spawning from the top corners in a circular arc motion.
	/// </summary>
	private void AnimateHandsSpawn()
	{
		// Kill any existing animation
		if (_animationTween != null && _animationTween.IsValid())
		{
			_animationTween.Kill();
		}
		
		var viewportSize = GetViewport().GetVisibleRect().Size;
		
		// Set starting positions (top corners)
		Vector2 playerHandStartPos = new Vector2(viewportSize.X - 100, -100); // Top right
		Vector2 npcHandStartPos = new Vector2(-100, -100); // Top left
		
		_playerHand.Position = playerHandStartPos;
		_npcHand.Position = npcHandStartPos;
		
		// Set starting rotation (hands are rotated at start)
		_playerHand.Rotation = Mathf.DegToRad(-45); // Rotated clockwise
		_npcHand.Rotation = Mathf.DegToRad(45); // Rotated counter-clockwise
		
		// Create new tween for animation
		_animationTween = CreateTween();
		_animationTween.SetParallel(true); // Run all animations in parallel
		_animationTween.SetEase(Tween.EaseType.Out);
		_animationTween.SetTrans(Tween.TransitionType.Back);
		
		// Animate player hand (from top right to center right)
		_animationTween.TweenProperty(_playerHand, "position", _playerHandFinalPosition, _animationDuration);
		_animationTween.TweenProperty(_playerHand, "rotation", 0.0f, _animationDuration);
		
		// Animate NPC hand (from top left to center left)
		_animationTween.TweenProperty(_npcHand, "position", _npcHandFinalPosition, _animationDuration);
		_animationTween.TweenProperty(_npcHand, "rotation", 0.0f, _animationDuration);
	}

	/// <summary>
	/// Gets the asset path for the player's hand.
	/// </summary>
	private string GetPlayerHandAsset(string move)
	{
		return move switch
		{
			"scissors" => "res://assets/ai-scissors-hand-player.png",
			"paper" => "res://assets/ai-paper-hand-nobg.png",
			"rock" => "res://assets/ai-rock-hand-player.png",
			_ => "res://assets/ai-paper-hand-nobg.png"
		};
	}

	/// <summary>
	/// Gets the asset path for the NPC's hand.
	/// </summary>
	private string GetNPCHandAsset(string move)
	{
		return move switch
		{
			"scissors" => "res://assets/ai-scissors-hand-npc.png",
			"paper" => "res://assets/ai-paper-hand-npc.png",
			"rock" => "res://assets/ai-rock-hand-npc.png",
			_ => "res://assets/ai-paper-hand-npc.png"
		};
	}

	/// <summary>
	/// Determines what hand the NPC should throw based on the player's move and win condition.
	/// </summary>
	private string DetermineNPCMove(string playerMove, bool playerWins)
	{
		playerMove = playerMove.ToLower();

		string npcMove;
		if (playerWins)
		{
			// Player wins - NPC throws the losing move
			npcMove = playerMove switch
			{
				"rock" => "scissors",   // Rock beats scissors - player wins
				"paper" => "rock",      // Paper beats rock - player wins
				"scissors" => "paper",  // Scissors beats paper - player wins
				_ => "scissors"
			};
			GD.Print($"[RPSResultOverlay] Player WINS - Player: {playerMove}, NPC: {npcMove}");
		}
		else
		{
			// Player loses - NPC throws the winning move
			npcMove = playerMove switch
			{
				"rock" => "paper",      // Paper beats rock - player loses
				"paper" => "scissors",  // Scissors beats paper - player loses
				"scissors" => "rock",   // Rock beats scissors - player loses
				_ => "paper"
			};
			GD.Print($"[RPSResultOverlay] Player LOSES - Player: {playerMove}, NPC: {npcMove}");
		}
		
		return npcMove;
	}
}
