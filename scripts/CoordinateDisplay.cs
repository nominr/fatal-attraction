// using Godot;
// using System;

// /// <summary>
// /// Displays the player's current coordinates on screen as they move.
// /// Shows X and Y coordinates in the top-left corner.
// /// </summary>
// public partial class CoordinateDisplay : CanvasLayer
// {
// 	private Label _coordinateLabel;
// 	private PlayerController _playerController;
// 	private Color _labelColor = Colors.White;
// 	private Vector2 _labelPosition = new Vector2(20, 20);
// 	private float _fontSize = 24;

// 	public override void _Ready()
// 	{
// 		SetupUI();
		
// 		// Find the local player controller - try multiple approaches
// 		// First, try to get it from GameWorld directly
// 		var gameWorld = GetParent() as GameWorld;
// 		if (gameWorld != null)
// 		{
// 			// Access the local player through reflection or wait for it to be set
// 			var localPlayerField = gameWorld.GetType().GetField("_localPlayer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
// 			if (localPlayerField != null)
// 			{
// 				_playerController = localPlayerField.GetValue(gameWorld) as PlayerController;
// 			}
// 		}
		
// 		// If still not found, try searching in players group
// 		if (_playerController == null)
// 		{
// 			var playersInGroup = GetTree().GetNodesInGroup("players");
// 			if (playersInGroup.Count > 0)
// 			{
// 				_playerController = playersInGroup[0] as PlayerController;
// 			}
// 		}
		
// 		if (_playerController == null)
// 		{
// 			GD.PrintErr("CoordinateDisplay: Could not find PlayerController!");
// 		}
// 		else
// 		{
// 			GD.Print("CoordinateDisplay: PlayerController found and ready!");
// 		}
// 	}

// 	private void SetupUI()
// 	{
// 		// Create label for coordinate display
// 		_coordinateLabel = new Label();
// 		_coordinateLabel.AddThemeColorOverride("font_color", _labelColor);
// 		_coordinateLabel.AddThemeFontSizeOverride("font_size", (int)_fontSize);
// 		_coordinateLabel.Position = _labelPosition;
// 		_coordinateLabel.Text = "Coordinates: (0, 0)";
		
// 		// Add outline effect for better visibility
// 		var labelSettings = new LabelSettings();
// 		labelSettings.OutlineSize = 2;
// 		labelSettings.OutlineColor = Colors.Black;
// 		_coordinateLabel.LabelSettings = labelSettings;
		
// 		AddChild(_coordinateLabel);
// 	}

// 	public override void _Process(double delta)
// 	{
// 		if (_playerController == null)
// 		{
// 			// Try again if not found yet
// 			var playersInGroup = GetTree().GetNodesInGroup("players");
// 			if (playersInGroup.Count > 0)
// 			{
// 				_playerController = playersInGroup[0] as PlayerController;
// 			}
// 			return;
// 		}

// 		// Get player position and round to integers for cleaner display
// 		Vector2 playerPos = _playerController.GlobalPosition;
// 		int x = (int)Math.Round(playerPos.X);
// 		int y = (int)Math.Round(playerPos.Y);

// 		// Update label text
// 		_coordinateLabel.Text = $"Coordinates: ({x}, {y})";
// 	}

// 	/// <summary>
// 	/// Change the color of the coordinate display
// 	/// </summary>
// 	public void SetLabelColor(Color color)
// 	{
// 		_labelColor = color;
// 		if (_coordinateLabel != null)
// 			_coordinateLabel.AddThemeColorOverride("font_color", _labelColor);
// 	}

// 	/// <summary>
// 	/// Change the position of the coordinate display on screen
// 	/// </summary>
// 	public void SetLabelPosition(Vector2 position)
// 	{
// 		_labelPosition = position;
// 		if (_coordinateLabel != null)
// 			_coordinateLabel.Position = _labelPosition;
// 	}

// 	/// <summary>
// 	/// Change the font size of the coordinate display
// 	/// </summary>
// 	public void SetFontSize(float size)
// 	{
// 		_fontSize = size;
// 		if (_coordinateLabel != null)
// 			_coordinateLabel.AddThemeFontSizeOverride("font_size", (int)_fontSize);
// 	}
// }
