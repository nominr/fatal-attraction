using Godot;
using System;

/// <summary>
/// Player controller for spatial movement in the game world.
/// Uses CharacterBody2D for physics-based movement.
/// </summary>
public partial class PlayerController : CharacterBody2D
{
	[Signal]
	public delegate void PositionChangedEventHandler(long playerId, Vector2 position);

	[Export]
	public float Speed { get; set; } = 200.0f;

	[Export]
	public Color PlayerColor { get; set; } = Colors.Green;

	[Export]
	public string PlayerRole { get; set; } = "Observer";

	[Export]
	public int PlayerIndex { get; set; } = 1; // 1, 2, or 3 for sprite selection

	// Visual elements
	private Node2D _roleVisuals;
	private Camera2D _camera;
	private Label _nameLabel;
	private Label _roleLabel;

	// Networking
	private bool _isLocalPlayer = false;
	private long _playerId = 0;
	private Vector2 _lastSentPosition = Vector2.Zero;
	private const float PositionSyncThreshold = 2.0f; // Only sync if moved more than this

	public override void _Ready()
	{
		// Critical for top-down movement: Disable gravity logic
		MotionMode = MotionModeEnum.Floating;
		
		SetupVisuals();
		AddToGroup("players");
	}

	private void SetupVisuals()
	{
		// Role label below player
		_roleLabel = new Label();
		_roleLabel.Text = PlayerRole;
		_roleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_roleLabel.Position = new Vector2(-40, 20);
		_roleLabel.CustomMinimumSize = new Vector2(80, 20);
		AddChild(_roleLabel);

		// Load visual scene for role
		LoadRoleScene(PlayerRole);
	}

	private void LoadRoleScene(string role)
	{
		// Remove existing visuals
		if (_roleVisuals != null)
		{
			_roleVisuals.QueueFree();
			_roleVisuals = null;
		}

		string scenePath = $"res://scenes/{role.ToLower()}.tscn";
		var scene = GD.Load<PackedScene>(scenePath);

		if (scene != null)
		{
			_roleVisuals = scene.Instantiate() as Node2D;
			if (_roleVisuals != null)
			{
				AddChild(_roleVisuals);
				GD.Print($"Loaded player scene for {role}: {scenePath}");

				// Find Camera2D
				_camera = _roleVisuals.GetNodeOrNull<Camera2D>("Camera2D") ?? FindNodeByType<Camera2D>(_roleVisuals);
				if (_camera != null)
				{
					_camera.Enabled = _isLocalPlayer;
				}
				else
				{
					GD.Print($"Warning: No Camera2D found in {role} scene");
				}
			}
		}
		else
		{
			GD.PrintErr($"Failed to load scene: {scenePath}");
			// Fallback: Create simple sprite
			_roleVisuals = new Sprite2D();
			var fallbackTexture = new GradientTexture2D();
			fallbackTexture.Width = 32;
			fallbackTexture.Height = 32;
			((Sprite2D)_roleVisuals).Texture = fallbackTexture;
			AddChild(_roleVisuals);
		}
	}

	private T FindNodeByType<T>(Node root) where T : Node
	{
		if (root is T t) return t;
		foreach (Node child in root.GetChildren())
		{
			var found = FindNodeByType<T>(child);
			if (found != null) return found;
		}
		return null;
	}

	public void SetLocalPlayer(bool isLocal)
	{
		_isLocalPlayer = isLocal;
		GD.Print($"SetLocalPlayer called: isLocal={isLocal}");
		
		if (_camera != null)
		{
			_camera.Enabled = isLocal;
		}
		
		// Optional: Hide label for self or emphasize
		if (_roleVisuals != null)
		{
			_roleVisuals.Modulate = isLocal ? Colors.White : new Color(0.8f, 0.8f, 0.8f, 1f);
		}
	}

	public void SetRole(string role)
	{
		PlayerRole = role;
		if (_roleLabel != null) _roleLabel.Text = role;
		LoadRoleScene(role);
	}

	public void SetColor(Color color)
	{
		PlayerColor = color;
		// If scene uses modulation or specific parts, handle here.
		// For now, minimal impact as scenes are pre-made.
	}

	/// <summary>
	/// Set the network player ID for this controller
	/// </summary>
	public void SetPlayerId(long playerId)
	{
		_playerId = playerId;
		_lastSentPosition = Position; // Initialize to current position
		GD.Print($"PlayerController: Set player ID {playerId} at position {Position}");
	}

	/// <summary>
	/// Update position from network sync (for remote players)
	/// </summary>
	public void UpdateRemotePosition(Vector2 newPosition)
	{
		if (_isLocalPlayer) return; // Don't override local player position
		GD.Print($"PlayerController: Updating remote player {_playerId} position to {newPosition}");
		Position = newPosition;
	}

	public override void _PhysicsProcess(double delta)
	{
		// Only process input for the local player
		if (!_isLocalPlayer) return;

		var velocity = Vector2.Zero;

		// Handle input
		if (Input.IsActionPressed("ui_right") || Input.IsKeyPressed(Key.D))
		{
			velocity.X += 1;
			GD.Print("Right Input Detected");
		}
		if (Input.IsActionPressed("ui_left") || Input.IsKeyPressed(Key.A))
		{
			velocity.X -= 1;
			GD.Print("Left Input Detected");
		}
		if (Input.IsActionPressed("ui_down") || Input.IsKeyPressed(Key.S))
		{
			velocity.Y += 1;
			GD.Print("Down Input Detected");
		}
		if (Input.IsActionPressed("ui_up") || Input.IsKeyPressed(Key.W))
		{
			velocity.Y -= 1;
			GD.Print("Up Input Detected");
		}

		if (velocity.Length() > 0)
		{
			velocity = velocity.Normalized() * Speed;
		}

		Velocity = velocity;
		MoveAndSlide();

		// Emit position change if moved significantly
		if (Position.DistanceTo(_lastSentPosition) > PositionSyncThreshold)
		{
			_lastSentPosition = Position;
			GD.Print($"PlayerController: Player {_playerId} emitting position {Position}");
			EmitSignal(SignalName.PositionChanged, _playerId, Position);
		}
	}
}
