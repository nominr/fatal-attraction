using Godot;
using System;
using System.Collections.Generic;

public partial class TriangleScene : Control
{
	/// <summary>
	/// Dynamic storage for live NPC states.
	/// </summary>
	public Dictionary<string, Vector2> ActiveNpcStates { get; private set; } = new();

	private List<Sprite2D> _allPointsDots = new();


	private Sprite2D _pointSprite;
	private Tween _movementTween;
	
	// Outer triangle vertices (local coords, before center offset)
	private readonly Vector2 _v1 = new Vector2(-30, 24);   // Bottom-left  (Prophet)
	private readonly Vector2 _v2 = new Vector2(30, 24);    // Bottom-right (Producer)
	private readonly Vector2 _v3 = new Vector2(0, -28);    // Top          (Admirer)
	private readonly Vector2 _centerOffset = new Vector2(77, 82); // Position of Polygon2D/TriangleUi

	// Inner medial triangle: midpoints of outer triangle edges
	private Vector2 _m12; // midpoint of v1-v2 (bottom edge)
	private Vector2 _m13; // midpoint of v1-v3 (left edge)
	private Vector2 _m23; // midpoint of v2-v3 (right edge)

	// Zone colors (semi-transparent tints)
	private readonly Color _prophetZoneColor  = new Color(0.3f, 0.5f, 1.0f, 0.18f); // Blue tint
	private readonly Color _producerZoneColor = new Color(1.0f, 0.3f, 0.3f, 0.18f); // Red tint
	private readonly Color _admirerZoneColor  = new Color(0.3f, 1.0f, 0.5f, 0.18f); // Green tint
	private readonly Color _boundaryColor     = new Color(1.0f, 1.0f, 1.0f, 0.55f); // White lines

	// ── Zone membership tracking ────────────────────────────────────
	public List<string> ProphetZoneNpcs  { get; private set; } = new();
	public List<string> AdmirerZoneNpcs  { get; private set; } = new();
	public List<string> ProducerZoneNpcs { get; private set; } = new();
	public List<string> NeutralZoneNpcs  { get; private set; } = new();

	public int ProphetZoneCount  => ProphetZoneNpcs.Count;
	public int AdmirerZoneCount  => AdmirerZoneNpcs.Count;
	public int ProducerZoneCount => ProducerZoneNpcs.Count;
	public int NeutralZoneCount  => NeutralZoneNpcs.Count;

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

		// Compute inner medial triangle midpoints
		_m12 = (_v1 + _v2) / 2f; // (0, 24)   — bottom midpoint
		_m13 = (_v1 + _v3) / 2f; // (-15, -2) — left midpoint
		_m23 = (_v2 + _v3) / 2f; // (15, -2)  — right midpoint

		ComputeZoneMembership();
	}

	/// <summary>
	/// Updates the live NPC states and refreshes zone membership.
	/// </summary>
	public void UpdateActiveStates(Dictionary<string, Vector2> states)
	{
		ActiveNpcStates = states;
		ComputeZoneMembership();
	}

	/// <summary>
	/// Classifies every NPC in ActiveNpcStates into its influence zone.
	/// Populates ProphetZoneNpcs, AdmirerZoneNpcs, ProducerZoneNpcs, NeutralZoneNpcs.
	/// </summary>
	public void ComputeZoneMembership()
	{
		ProphetZoneNpcs.Clear();
		AdmirerZoneNpcs.Clear();
		ProducerZoneNpcs.Clear();
		NeutralZoneNpcs.Clear();

		foreach (var kvp in ActiveNpcStates)
		{
			string zone = GetInfluenceZone(kvp.Value);
			switch (zone)
			{
				case "prophet":  ProphetZoneNpcs.Add(kvp.Key);  break;
				case "admirer":  AdmirerZoneNpcs.Add(kvp.Key);  break;
				case "producer": ProducerZoneNpcs.Add(kvp.Key); break;
				default:         NeutralZoneNpcs.Add(kvp.Key);  break;
			}
		}
	}

	
	public void Show(string npcName, Vector2? pointPos = null)
	{
		if (_npcNameLabel != null)
		{
			// HIDE NPC NAME LABEL
			if (_npcNameLabel != null)
			{
				_npcNameLabel.Visible = false;
			}
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
	
	/// <summary>
	/// Displays all NPC state vectors from ActiveNpcStates as individual points.
	/// Hides the single-point sprite and NPC name label.
	/// </summary>
	public void ShowAllPoints()
	{
		// Hide single-point sprite and name label
		if (_pointSprite != null) _pointSprite.Visible = false;
		if (_npcNameLabel != null) _npcNameLabel.Visible = false;

		// Clear existing dots
		foreach (var dot in _allPointsDots)
		{
			dot.QueueFree();
		}
		_allPointsDots.Clear();

		var heartTexture = GD.Load<Texture2D>("res://assets/black-heart-ui.png");

		foreach (var kvp in ActiveNpcStates)
		{
			var dot = new Sprite2D();
			dot.Texture = heartTexture;
			dot.Scale = new Vector2(0.45f, 0.45f); // Small dots for multi-point view
			dot.Position = _centerOffset + kvp.Value;
			dot.ZIndex = 1;
			AddChild(dot);
			_allPointsDots.Add(dot);
		}

		Visible = true;
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

	// ── Zone of Influence Drawing ──────────────────────────────────

	public override void _Draw()
	{
		// All positions are in screen space (offset by _centerOffset)
		Vector2 ov1 = _centerOffset + _v1;  // outer bottom-left  (Prophet)
		Vector2 ov2 = _centerOffset + _v2;  // outer bottom-right (Producer)
		Vector2 ov3 = _centerOffset + _v3;  // outer top          (Admirer)

		Vector2 im12 = _centerOffset + _m12; // inner bottom
		Vector2 im13 = _centerOffset + _m13; // inner left
		Vector2 im23 = _centerOffset + _m23; // inner right

		// Draw the 3 corner zone tints as filled polygons
		// Prophet zone: outer bottom-left corner (v1, m12, m13)
		DrawPolygon(new Vector2[] { ov1, im12, im13 }, new Color[] { _prophetZoneColor, _prophetZoneColor, _prophetZoneColor });

		// Producer zone: outer bottom-right corner (v2, m12, m23)
		DrawPolygon(new Vector2[] { ov2, im12, im23 }, new Color[] { _producerZoneColor, _producerZoneColor, _producerZoneColor });

		// Admirer zone: outer top corner (v3, m13, m23)
		DrawPolygon(new Vector2[] { ov3, im13, im23 }, new Color[] { _admirerZoneColor, _admirerZoneColor, _admirerZoneColor });

		// Draw inner triangle boundary lines
		float lineWidth = 1.5f;
		DrawLine(im12, im13, _boundaryColor, lineWidth);
		DrawLine(im13, im23, _boundaryColor, lineWidth);
		DrawLine(im23, im12, _boundaryColor, lineWidth);
	}

	// ── Zone Query ─────────────────────────────────────────────────

	/// <summary>
	/// Returns which player's zone of influence the given local point
	/// (relative to triangle origin, WITHOUT center offset) falls in.
	/// Returns "prophet", "producer", "admirer", or "neutral".
	/// </summary>
	public string GetInfluenceZone(Vector2 point)
	{
		// If inside the inner medial triangle → neutral
		if (PointInTriangle(point, _m12, _m13, _m23))
			return "neutral";

		// Check each corner sub-triangle
		if (PointInTriangle(point, _v1, _m12, _m13))
			return "prophet";

		if (PointInTriangle(point, _v2, _m12, _m23))
			return "producer";

		if (PointInTriangle(point, _v3, _m13, _m23))
			return "admirer";

		// Outside the triangle entirely
		return "neutral";
	}

	/// <summary>
	/// Standard sign-area point-in-triangle test.
	/// </summary>
	private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
	{
		float d1 = Sign(p, a, b);
		float d2 = Sign(p, b, c);
		float d3 = Sign(p, c, a);

		bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
		bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

		return !(hasNeg && hasPos);
	}

	private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
	{
		return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
	}
}
