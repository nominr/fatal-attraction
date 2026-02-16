using Godot;
using System;

public partial class NotificationItem : Control
{
    private Label _label;
    private TextureRect _background;
    private string _text;
    public double Duration { get; set; } = 3.0;
    public Vector2 StartPosition { get; set; }
    public Vector2 TargetPosition { get; set; }

    public event Action<NotificationItem> OnFinished;

    public static NotificationItem Create(string text, Font font, Vector2 startPos, Vector2 targetPos, double duration = 3.0)
    {
        var item = new NotificationItem();
        item._text = text;
        item.Duration = duration;
        item.StartPosition = startPos;
        item.TargetPosition = targetPos;
        return item;
    }

    public override void _Ready()
    {
        GD.Print($"[NotificationItem] Ready for text: '{_text}' StartPos: {StartPosition} TargetPos: {TargetPosition}");
        
        // Ensure it's very visible - on top of everything
        ZIndex = 1000;
        MouseFilter = MouseFilterEnum.Ignore; // Don't block clicks
        
        // Load the background texture
        Texture2D texture = null;
        try {
            texture = GD.Load<Texture2D>("res://assets/ai_message_box.png");
        } catch {}

        if (texture == null)
        {
            try {
                GD.Print("[NotificationItem] GD.Load failed, trying Image.LoadFromFile");
                var image = Image.LoadFromFile("res://assets/ai_message_box.png");
                if (image != null) texture = ImageTexture.CreateFromImage(image);
            } catch (Exception e) {
                GD.PrintErr($"[NotificationItem] Image.LoadFromFile failed: {e.Message}");
            }
        }

        // Calculate size based on texture or fallback
        Vector2 boxSize;
        if (texture != null)
        {
            Vector2 texSize = texture.GetSize();
            float baseWidth = 630f; // 20% wider than 525
            float scale = baseWidth / texSize.X;
            float height = texSize.Y * scale * 0.7f; // 30% shorter height
            boxSize = new Vector2(baseWidth, height);
            GD.Print($"[NotificationItem] Texture loaded. Size: {boxSize}");
        }
        else
        {
            boxSize = new Vector2(630, 110); // Same wider width, shorter height
            GD.PrintErr("[NotificationItem] Using fallback size.");
        }

        // Set the control size
        Size = boxSize;

        // Setup Background
        _background = new TextureRect();
        if (texture != null)
        {
            _background.Texture = texture;
        }
        _background.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _background.StretchMode = TextureRect.StretchModeEnum.Scale;
        _background.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_background);

        // Fallback dark background if no texture
        if (texture == null)
        {
            var fallbackColor = new ColorRect();
            fallbackColor.Color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            fallbackColor.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(fallbackColor);
        }

        // Use CenterContainer to reliably center the label
        ClipContents = true;
        
        var centerContainer = new CenterContainer();
        centerContainer.SetAnchorsPreset(LayoutPreset.FullRect);
        centerContainer.OffsetBottom = -boxSize.Y * 0.25f; // Shift center area upward
        centerContainer.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(centerContainer);
        
        _label = new Label();
        _label.Text = _text;
        try {
            _label.AddThemeFontOverride("font", GD.Load<Font>("res://assets/Pixer-Regular.otf"));
        } catch {
            GD.PrintErr("[NotificationItem] Failed to load font.");
        }
        _label.AddThemeFontSizeOverride("font_size", 28);
        _label.AddThemeColorOverride("font_color", Colors.Black);
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _label.CustomMinimumSize = new Vector2(boxSize.X - 40, 0);
        _label.MouseFilter = MouseFilterEnum.Ignore;
        centerContainer.AddChild(_label);

        GD.Print($"[NotificationItem] Label text: '{_label.Text}', boxSize: {boxSize}");

        // Set initial position (off-screen above)
        Position = StartPosition;
        GD.Print($"[NotificationItem] Initial position set to: {Position}, will tween to: {TargetPosition}");

        // Slide down animation
        var tween = CreateTween();
        tween.SetTrans(Tween.TransitionType.Back);
        tween.SetEase(Tween.EaseType.Out);
        tween.TweenProperty(this, "position", TargetPosition, 0.5);
        
        // Auto-remove: slide back up then free
        var timer = GetTree().CreateTimer(Duration);
        timer.Timeout += () => {
            GD.Print("[NotificationItem] Sliding out.");
            var outTween = CreateTween();
            outTween.SetTrans(Tween.TransitionType.Back);
            outTween.SetEase(Tween.EaseType.In);
            outTween.TweenProperty(this, "position", StartPosition, 0.4);
            outTween.Finished += () => {
                OnFinished?.Invoke(this);
                QueueFree();
            };
        };
    }
}
