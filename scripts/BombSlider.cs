using Godot;
using System;
using System.Collections.Generic;

public partial class BombSlider : Control
{
	public float SliderPosition { get; set; } = 0f;
	public List<Vector2> TargetZones { get; set; } = new List<Vector2>();
	
	private const float BAR_WIDTH = 400f;
	private const float BAR_HEIGHT = 30f;
	private const float HITBOX_EXTENSION = 10f; // Visual extension of target zones
	
	public override void _Draw()
	{
		Vector2 startPos = new Vector2(0, 5);
		
		// Draw main grey bar background
		DrawRect(new Rect2(startPos, new Vector2(BAR_WIDTH, BAR_HEIGHT)), new Color(0.4f, 0.4f, 0.4f, 1f));
		
		// Draw target zones (light green slivers) with extended hitbox
		foreach (var zone in TargetZones)
		{
			Vector2 zonePos = new Vector2(startPos.X + zone.X - HITBOX_EXTENSION, startPos.Y);
			Vector2 zoneSize = new Vector2(zone.Y + (HITBOX_EXTENSION * 2), BAR_HEIGHT);
			DrawRect(new Rect2(zonePos, zoneSize), new Color(0.6f, 0.8f, 0.6f, 1f));
		}
		
		// Draw moving black vertical line
		Vector2 linePos = new Vector2(startPos.X + SliderPosition, startPos.Y);
		DrawLine(linePos, linePos + new Vector2(0, BAR_HEIGHT), Colors.Black, 3f);
	}
	
	public override void _Process(double delta)
	{
		// Trigger redraw every frame
		QueueRedraw();
	}
}
