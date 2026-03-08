using Godot;
using System;
using System.Collections.Generic;

public partial class TriangleScene : Control
{
	/// <summary>
	/// Dynamic storage for live NPC states.
	/// </summary>
	public Dictionary<string, Vector2> ActiveNpcStates { get; private set; } = new();

	// Overlay for drawing lines on top of sprites
	private Control _linesOverlay;

	private List<Sprite2D> _allPointsDots = new();


	private Sprite2D _pointSprite;
	private Tween _movementTween;
	
	// Outer triangle vertices (local coords, before center offset)
	private readonly Vector2 _v1 = new Vector2(-30, 24);   // Bottom-left  (Prophet)
	private readonly Vector2 _v2 = new Vector2(30, 24);    // Bottom-right (Producer)
	private readonly Vector2 _v3 = new Vector2(0, -28);    // Top          (Admirer)
	private readonly Vector2 _centerOffset = new Vector2(77, 82); // Position of Polygon2D/TriangleUi

	// Zone threshold points (2/3 along the edge from the corner)
	private Vector2 _p_prophet_producer; // Y=2/3 on bottom edge
	private Vector2 _p_prophet_admirer;  // Y=2/3 on left edge
	private Vector2 _p_producer_prophet; // Z=2/3 on bottom edge
	private Vector2 _p_producer_admirer; // Z=2/3 on right edge
	private Vector2 _p_admirer_prophet;  // X=2/3 on left edge
	private Vector2 _p_admirer_producer; // X=2/3 on right edge

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
		// Note: GameWorld.cs scales the parent _triangleScene by 2.0x, so effective scale = this × 2.
		_pointSprite.Scale = new Vector2(0.4f, 0.4f);
		_pointSprite.ZIndex = 1; // Ensure it renders above the triangle base
		// Initial position (center of triangle for now, or just hidden)
		_pointSprite.Position = _centerOffset; // Start at center offset
		AddChild(_pointSprite);
		
		// Disable mouse input to prevent blocking UI
		MouseFilter = MouseFilterEnum.Ignore;
		
		// Create overlay for lines to ensure they draw ON TOP of the sprite
		_linesOverlay = new Control();
		_linesOverlay.MouseFilter = MouseFilterEnum.Ignore;
		_linesOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
		_linesOverlay.Draw += OnDrawOverlay;
		AddChild(_linesOverlay);
		
		_npcNameLabel = GetNodeOrNull<Label>("NpcNameLabel");

		// Compute 2/3 threshold border points
		_p_prophet_producer = (_v1 * 2f + _v2) / 3f;
		_p_prophet_admirer  = (_v1 * 2f + _v3) / 3f;
		
		_p_producer_prophet = (_v2 * 2f + _v1) / 3f;
		_p_producer_admirer = (_v2 * 2f + _v3) / 3f;

		_p_admirer_prophet  = (_v3 * 2f + _v1) / 3f;
		_p_admirer_producer = (_v3 * 2f + _v2) / 3f;

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
		
		// Always enforce scale each time Show() is called (not just _Ready)
		if (_pointSprite != null)
			_pointSprite.Scale = new Vector2(0.4f, 0.4f);

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

		// Group NPCs by position (round to 0.5 so near-identical coords merge)
		var positionCounts = new Dictionary<Vector2, int>();
		foreach (var kvp in ActiveNpcStates)
		{
			// Snap to 0.5-unit grid so positions within 0.25 units collapse together
			var snapped = new Vector2(
				Mathf.Round(kvp.Value.X * 2f) / 2f,
				Mathf.Round(kvp.Value.Y * 2f) / 2f);
			positionCounts[snapped] = positionCounts.GetValueOrDefault(snapped, 0) + 1;
		}

		const float baseScale = 0.12f; // Reduced — hearts were too large in the global influence triangle

		foreach (var kvp in positionCounts)
		{
			int count = kvp.Value;
			var dot = new Sprite2D();
			dot.Texture = heartTexture;
			// Scale: 1 NPC = 0.45, each extra NPC adds 0.5× (2→×1.5, 3→×2.0, 4→×2.5 …)
			float s = baseScale * (1f + (count - 1) * 0.5f);
			dot.Scale = new Vector2(s, s);
			dot.Position = _centerOffset + kvp.Key;
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

	// ── Overlay Drawing ────────────────────────────────────────────
	private void OnDrawOverlay()
	{
		// Zone boundary lines removed
	}

	// ── Zone of Influence Drawing ──────────────────────────────────

	public override void _Draw()
	{
		// All positions are in screen space (offset by _centerOffset)
		Vector2 ov1 = _centerOffset + _v1;  // outer bottom-left  (Prophet)
		Vector2 ov2 = _centerOffset + _v2;  // outer bottom-right (Producer)
		Vector2 ov3 = _centerOffset + _v3;  // outer top          (Admirer)

		Vector2 p_prophet_prod = _centerOffset + _p_prophet_producer;
		Vector2 p_prophet_adm  = _centerOffset + _p_prophet_admirer;
		Vector2 p_prod_proph   = _centerOffset + _p_producer_prophet;
		Vector2 p_prod_adm     = _centerOffset + _p_producer_admirer;
		Vector2 p_adm_proph    = _centerOffset + _p_admirer_prophet;
		Vector2 p_adm_prod     = _centerOffset + _p_admirer_producer;

		// Draw the 3 corner zone tints as filled polygons
		// Prophet zone
		DrawPolygon(new Vector2[] { ov1, p_prophet_prod, p_prophet_adm }, new Color[] { _prophetZoneColor, _prophetZoneColor, _prophetZoneColor });
		// Producer zone
		DrawPolygon(new Vector2[] { ov2, p_prod_proph, p_prod_adm }, new Color[] { _producerZoneColor, _producerZoneColor, _producerZoneColor });
		// Admirer zone
		DrawPolygon(new Vector2[] { ov3, p_adm_proph, p_adm_prod }, new Color[] { _admirerZoneColor, _admirerZoneColor, _admirerZoneColor });

		// Draw inner triangle boundary lines
		// Moved to _linesOverlay to draw on top of sprite
		_linesOverlay?.QueueRedraw();
	}

	// ── Zone Query ─────────────────────────────────────────────────

	/// <summary>
	/// Returns which player's zone of influence the given local point
	/// (relative to triangle origin, WITHOUT center offset) falls in.
	/// Returns "prophet", "producer", "admirer", or "neutral".
	/// </summary>
	public string GetInfluenceZone(Vector2 point)
	{
		// Check each corner sub-triangle
		if (PointInTriangle(point, _v1, _p_prophet_producer, _p_prophet_admirer))
			return "prophet";

		if (PointInTriangle(point, _v2, _p_producer_prophet, _p_producer_admirer))
			return "producer";

		if (PointInTriangle(point, _v3, _p_admirer_prophet, _p_admirer_producer))
			return "admirer";

		// Outside the corner regions -> neutral
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

		// Epsilon tolerance ensures points EXACTLY on the boundary (or slightly off due to float math)
		// are counted as inside the triangle.
		float epsilon = 0.1f;
		bool hasNeg = (d1 < -epsilon) || (d2 < -epsilon) || (d3 < -epsilon);
		bool hasPos = (d1 > epsilon) || (d2 > epsilon) || (d3 > epsilon);

		return !(hasNeg && hasPos);
	}

	private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
	{
		return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
	}
	public Color GetGradientColor(Vector2 point)
	{
		// Barycentric weights for interpolation
		// We need weights w1, w2, w3 for v1, v2, v3
		// P = w1*v1 + w2*v2 + w3*v3
		// w1 + w2 + w3 = 1
		
		float den = (_v2.Y - _v3.Y) * (_v1.X - _v3.X) + (_v3.X - _v2.X) * (_v1.Y - _v3.Y);
		float w1 = ((_v2.Y - _v3.Y) * (point.X - _v3.X) + (_v3.X - _v2.X) * (point.Y - _v3.Y)) / den;
		float w2 = ((_v3.Y - _v1.Y) * (point.X - _v3.X) + (_v1.X - _v3.X) * (point.Y - _v3.Y)) / den;
		float w3 = 1 - w1 - w2;
		
		// Clamp weights to [0,1] to keep color within triangle logic, or allow subtle over-saturation?
		// Clamping ensures we don't get wild colors outside the triangle.
		w1 = Mathf.Clamp(w1, 0f, 1f);
		w2 = Mathf.Clamp(w2, 0f, 1f);
		w3 = Mathf.Clamp(w3, 0f, 1f);
		
		// Re-normalize sum to 1 after clamping to prevent dark colors
		float sum = w1 + w2 + w3;
		if (sum > 0)
		{
			w1 /= sum;
			w2 /= sum;
			w3 /= sum;
		}

		// Colors
		// v1 = Bottom-Left (Prophet) -> Blue
		// v2 = Bottom-Right (Producer) -> Green
		// v3 = Top (Admirer) -> Red
		// Center -> White (naturally comes from mixing R+G+B)
		
		Color cProphet = Colors.Blue;
		Color cProducer = Colors.Green;
		Color cAdmirer = Colors.Red;
		
		// Mix
		float r = w1 * cProphet.R + w2 * cProducer.R + w3 * cAdmirer.R;
		float g = w1 * cProphet.G + w2 * cProducer.G + w3 * cAdmirer.G;
		float b = w1 * cProphet.B + w2 * cProducer.B + w3 * cAdmirer.B;
		
		// To ensure the center is White, we might need to adjust the mix. 
		// Pure R+G+B = White.
		// w1=w2=w3=0.33 -> 0.33R + 0.33G + 0.33B = Dark Grey? No.
		// Blue=(0,0,1), Green=(0,1,0), Red=(1,0,0).
		// Sum = (0.33, 0.33, 0.33) -> Dark Grey.
		// The user wants the center to be White.
		
		// Improved Logic:
		// Interpolate towards White based on distance from corners?
		// Or assume the "Center" color is White and interpolate from Corner to Center?
		
		// Alternative:
		// Map position to:
		// Radius from center?
		
		// Let's use a 4-point interpolation or a "Colorize" approach.
		// Center of triangle is (0,0) approx?
		// Centroid = (v1+v2+v3)/3 = (-30+30+0, 24+24-28)/3 = (0, 20/3) = (0, 6.66).
		// Wait, local origin (0,0) is likely the centroid if defined around it?
		// _v1=(-30, 24), _v2=(30, 24), _v3=(0, -28).
		// Centroid Y = (24+24-28)/3 = 20/3 = 6.66.
		// Centroid X = 0.
		// So (0, 6.66) is the geometric center.
		
		// Calculate distance from each corner to determine "influence" of that color.
		// And distance from center to determine "whiteness".
		
		// Let's stick to the user's specific request: "triangle is a gradient... center being white".
		// This means as we move from a corner to the center, we go from Color -> White.
		
		// Approach:
		// 1. Calculate barycentric weights (w1, w2, w3).
		// 2. Identify primary influence (max weight).
		// 3. Interpolate between PrimaryColor and White based on how "centered" we are?
		// Actually, standard mixing of R(1,0,0), G(0,1,0), B(0,0,1) gives (0.33,0.33,0.33) at center.
		// We want (1,1,1) at center.
		// So we need to ADD a base white component?
		
		// Additive blending? 
		// Color = w1*C1 + w2*C2 + w3*C3 + BaseWhite * (1 - DistanceFromCenter)?
		
		// Let's try boosting the values so sum is close to 1.
		// Or simpler:
		// Map the barycentric weights to Hue? 
		// And Saturation drops to 0 at center?
		
		// Let's use Saturation.
		// Corners are S=1. Center is S=0 (White).
		// Saturation = Distance from Center / MaxDistance?
		// Centroid is approx (0, 5).
		// Max dist is approx 35.
		
		Vector2 centroid = (_v1 + _v2 + _v3) / 3.0f;
		float dist = point.DistanceTo(centroid);
		float maxDist = 30.0f; // Approx distance to vertices
		float saturation = Mathf.Clamp(dist / maxDist, 0f, 1f);
		
		// Determine Hue based on angle?
		// Top(Red) is -90 deg. 
		// Right(Green) is +30 deg ?
		// Left(Blue) is +150 deg ?
		
		// Let's stick to blending but BOOST the white.
		// RGB Additive?
		// w1*Blue + w2*Green + w3*Red
		// At center (0.33, 0.33, 0.33).
		// If we scale by 3? -> (1, 1, 1).
		// At corner (1, 0, 0) * 3 -> (3, 0, 0) -> Red (clamped).
		// Intermediate (0.5, 0.5, 0) * 3 -> (1.5, 1.5, 0) -> Yellow (Red+Green).
		
		// This works! Scaling the weighted sum by ~3.0 makes the center white and corners pure.
		r *= 2.5f; // reduced slightly from 3 to allow some color at center
		g *= 2.5f;
		b *= 2.5f;
		
		// Optional: Add a base white floor?
		// float baseWhite = 0.2f;
		// r += baseWhite; g += baseWhite; b += baseWhite;
		
		return new Color(Mathf.Clamp(r, 0, 1), Mathf.Clamp(g, 0, 1), Mathf.Clamp(b, 0, 1), 1.0f);
	}
	
	public Color GetZoneColor(Vector2 point)
	{
		// Deprecated direct zone, use gradient
		return GetGradientColor(point);
	}
}
