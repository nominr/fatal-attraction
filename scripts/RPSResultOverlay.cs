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
	private float _displayTime = 2.0f;
	private float _elapsedTime = 0.0f;
	private bool _isDisplaying = false;

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
		_playerHand.AnchorLeft = 0.65f;
		_playerHand.AnchorTop = 0.3f;
		_playerHand.AnchorRight = 0.65f;
		_playerHand.AnchorBottom = 0.3f;
		_playerHand.OffsetLeft = -100;
		_playerHand.OffsetTop = -100;
		_playerHand.OffsetRight = 100;
		_playerHand.OffsetBottom = 100;
		_playerHand.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_playerHand.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_playerHand);

		// Create NPC hand display (left side)
		_npcHand = new TextureRect();
		_npcHand.AnchorLeft = 0.15f;
		_npcHand.AnchorTop = 0.3f;
		_npcHand.AnchorRight = 0.15f;
		_npcHand.AnchorBottom = 0.3f;
		_npcHand.OffsetLeft = -100;
		_npcHand.OffsetTop = -100;
		_npcHand.OffsetRight = 100;
		_npcHand.OffsetBottom = 100;
		_npcHand.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		_npcHand.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(_npcHand);
		
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
	public void Show(string playerMove, bool npcWins = true)
	{
		Visible = true;
		_isDisplaying = true;
		_elapsedTime = 0.0f;

		GD.Print($"[RPSResultOverlay] Showing overlay with player move: {playerMove}, NPC wins: {npcWins}");

		// Load player's hand texture (nobg version)
		string playerHandPath = $"res://assets/ai-{playerMove.ToLower()}-hand-nobg.png";
		GD.Print($"[RPSResultOverlay] Loading player hand: {playerHandPath}");
		if (ResourceLoader.Exists(playerHandPath))
		{
			_playerHand.Texture = ResourceLoader.Load<Texture2D>(playerHandPath);
			_playerHand.Visible = true;
			_playerHand.ZIndex = 100;
			
			// Set pivot offset after loading texture
			if (_playerHand.Texture != null)
			{
				_playerHand.PivotOffset = _playerHand.Texture.GetSize() / 2;
			}
			
			// Apply transformations for player's hand
			ApplyPlayerHandTransform(playerMove.ToLower());
			
			GD.Print($"[RPSResultOverlay] Player hand loaded, size: {_playerHand.Size}");
		}
		else
		{
			GD.PrintErr($"[RPSResultOverlay] Player hand texture not found: {playerHandPath}");
		}

		// Determine and load NPC's hand (nobg version)
		string npcMove = DetermineNPCMove(playerMove, npcWins);
		string npcHandPath = $"res://assets/ai-{npcMove.ToLower()}-hand-nobg.png";
		GD.Print($"[RPSResultOverlay] Loading NPC hand: {npcHandPath}");
		if (ResourceLoader.Exists(npcHandPath))
		{
			_npcHand.Texture = ResourceLoader.Load<Texture2D>(npcHandPath);
			_npcHand.Visible = true;
			_npcHand.ZIndex = 100;
			
			// Set pivot offset after loading texture
			if (_npcHand.Texture != null)
			{
				_npcHand.PivotOffset = _npcHand.Texture.GetSize() / 2;
			}
			
			// Apply transformations for NPC's hand
			ApplyNPCHandTransform(npcMove.ToLower());
			
			GD.Print($"[RPSResultOverlay] NPC hand loaded, size: {_npcHand.Size}");
		}
		else
		{
			GD.PrintErr($"[RPSResultOverlay] NPC hand texture not found: {npcHandPath}");
		}
	}

	/// <summary>
	/// Applies transformations to the player's hand based on the move type.
	/// </summary>
	private void ApplyPlayerHandTransform(string move)
	{
		// Reset transformations
		_playerHand.Rotation = 0;
		_playerHand.Scale = new Vector2(1, 1);

		switch (move)
		{
			case "scissors":
				// Rotate 90 degrees counterclockwise (-π/2 radians)
				_playerHand.Rotation = -Mathf.Pi / 2;
				break;
		}
	}

	/// <summary>
	/// Applies transformations to the NPC's hand based on the move type.
	/// </summary>
	private void ApplyNPCHandTransform(string move)
	{
		// Reset transformations
		_npcHand.Rotation = 0;
		_npcHand.Scale = new Vector2(1, 1);

		switch (move)
		{
			case "scissors":
				// Rotate 90 degrees counterclockwise
				_npcHand.Rotation = -Mathf.Pi / 2;
				
				// Create a shader material to flip horizontally
				var shaderMaterial = new ShaderMaterial();
				var shader = new Shader();
				shader.Code = @"
shader_type canvas_item;

void fragment() {
	vec2 uv = UV;
	uv.x = 1.0 - uv.x;
	COLOR = texture(TEXTURE, uv);
}
";
				shaderMaterial.Shader = shader;
				_npcHand.Material = shaderMaterial;
				break;
		}
	}

	/// <summary>
	/// Determines what hand the NPC should throw based on the player's move and win condition.
	/// </summary>
	private string DetermineNPCMove(string playerMove, bool npcWins)
	{
		playerMove = playerMove.ToLower();

		if (npcWins)
		{
			// NPC throws the winning move
			return playerMove switch
			{
				"rock" => "paper",      // Paper beats rock
				"paper" => "scissors",  // Scissors beats paper
				"scissors" => "rock",   // Rock beats scissors
				_ => "rock"
			};
		}
		else
		{
			// NPC throws the losing move
			return playerMove switch
			{
				"rock" => "scissors",   // Rock beats scissors
				"paper" => "rock",      // Paper beats rock
				"scissors" => "paper",  // Scissors beats paper
				_ => "paper"
			};
		}
	}
}
