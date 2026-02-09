using Godot;
using System;

public partial class TriangleScene : Control
{
	private Sprite2D _pointSprite;
	private Tween _movementTween;
	
	private readonly Vector2 _v1 = new Vector2(-30, 24);
	private readonly Vector2 _v2 = new Vector2(30, 24);
	private readonly Vector2 _v3 = new Vector2(0, -28);
	private readonly Vector2 _centerOffset = new Vector2(77, 82); // Position of Polygon2D/TriangleUi

	private Label _npcNameLabel;

	public override void _Ready()
	{
		// Instantiate the point sprite
		_pointSprite = new Sprite2D();
		_pointSprite.Texture = GD.Load<Texture2D>("res://assets/black-heart-ui.png");
		// Match TriangleUi scale (1.1)
		_pointSprite.Scale = new Vector2(1.1f, 1.1f);
		_pointSprite.ZIndex = 1; // Ensure it renders above the triangle base
		// Initial position (center of triangle for now, or just hidden)
		_pointSprite.Position = _centerOffset; // Start at center offset
		AddChild(_pointSprite);
		
		// Disable mouse input to prevent blocking UI
		MouseFilter = MouseFilterEnum.Ignore;
		
		_npcNameLabel = GetNodeOrNull<Label>("NpcNameLabel");
	}
	
	public void Show(string npcName, Vector2? pointPos = null)
	{
		if (_npcNameLabel != null)
		{
			_npcNameLabel.Text = npcName;
		}
		
		if (pointPos.HasValue)
		{
			// Position relative to center offset
			Vector2 targetPos = _centerOffset + pointPos.Value;
			
			// If already visible, animate to new position
			if (Visible)
			{
				AnimatePoint(_pointSprite.Position, targetPos);
			}
			else
			{
				// Snap immediately if first showing
				_pointSprite.Position = targetPos;
			}
		}
		else
		{
			_pointSprite.Position = _centerOffset; // Default center
		}
		
		Visible = true;
	}
	
	public void HideScene()
	{
		Visible = false;
	}



	/// <summary>
	/// Animates the point from startPos to endPos.
	/// If the scene is not visible, it snaps immediately without animation.
	/// </summary>
	public void AnimatePoint(Vector2 startPos, Vector2 endPos)
	{
		// If not visible in tree, snap immediately
		if (!IsVisibleInTree())
		{
			_pointSprite.Position = endPos;
			return;
		}

		// Kill previous tween if running
		if (_movementTween != null && _movementTween.IsRunning())
		{
			_movementTween.Kill();
		}

		_pointSprite.Position = startPos;
		_movementTween = CreateTween();
		
		// Animate using a simple ease
		_movementTween.TweenProperty(_pointSprite, "position", endPos, 0.5f)
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.Out);
	}
	
	private Vector2 GetRandomPointInTriangle()
	{
		// Barycentric coordinates
		float r1 = GD.Randf();
		float r2 = GD.Randf();
		
		if (r1 + r2 > 1)
		{
			r1 = 1 - r1;
			r2 = 1 - r2;
		}
		
		float r3 = 1 - r1 - r2;
		
		return (_v1 * r1) + (_v2 * r2) + (_v3 * r3);
	}
}
