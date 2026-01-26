using Godot;
using System;

public partial class InfoScene : Control
{
    private Label _nextLabel;
    private Label _backLabel;
    private Label _infoTextLabel;
    private Sprite2D _background;
    private Sprite2D _tvLobby;
    private Timer _backgroundTimer;
    private Texture2D _bg1;
    private Texture2D _bg2;
    private bool _showingBg1 = true;
    
    // Array of text messages to cycle through
    private string[] _textMessages = new string[]
    {
        "Welcome to Fatal Attraction!",
        "You've been invited to join the hottest new reality dating show, Love in Paradise!",
        "Play as the Admirer, killing your love interest's potential suitors to win their love...",
        "Or play as the Prophet, using the powers of chaos to save the world...",
        "Or play as the Producer, using your charm to win over the audience and catch the Admirer and Prophet in action.",
        // "Click Next to continue...",
    };
    
    private int _currentTextIndex = 0;
    
    public override void _Ready()
    {
        _nextLabel = GetNode<Label>("NextLabel");
        _backLabel = GetNode<Label>("BackLabel");
        _infoTextLabel = GetNode<Label>("InfoText");
        _background = GetNode<Sprite2D>("Background");
        _tvLobby = GetNode<Sprite2D>("TvLobby");
        _backgroundTimer = GetNode<Timer>("BackgroundTimer");
        
        // Load both background textures
        _bg1 = GD.Load<Texture2D>("res://assets/Title_BG_1.png");
        _bg2 = GD.Load<Texture2D>("res://assets/Title_BG_2.png");
        
        // Make the labels clickable
        _nextLabel.MouseFilter = MouseFilterEnum.Stop;
        _backLabel.MouseFilter = MouseFilterEnum.Stop;
        
        // Connect timer for background animation
        _backgroundTimer.Timeout += OnBackgroundTimerTimeout;
        
        // Set initial text
        UpdateInfoText();
        
        // Start with TV and text invisible for fade-in
        _tvLobby.Modulate = new Color(1, 1, 1, 0);
        _infoTextLabel.Modulate = new Color(1, 1, 1, 0);
        
        // Fade in TV and text
        var tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(_tvLobby, "modulate:a", 1.0f, 0.8f);
        tween.TweenProperty(_infoTextLabel, "modulate:a", 1.0f, 0.8f);
    }
    
    private void UpdateInfoText()
    {
        if (_currentTextIndex < _textMessages.Length)
        {
            _infoTextLabel.Text = _textMessages[_currentTextIndex];
        }
    }
    
    private void OnBackgroundTimerTimeout()
    {
        // Alternate between the two background images
        _showingBg1 = !_showingBg1;
        _background.Texture = _showingBg1 ? _bg1 : _bg2;
    }
    
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent && mouseEvent.Pressed)
        {
            // Check if click is within the Next label bounds
            if (_nextLabel != null)
            {
                var labelRect = _nextLabel.GetGlobalRect();
                if (labelRect.HasPoint(mouseEvent.Position))
                {
                    OnNextClicked();
                }
            }
            
            // Check if click is within the Back label bounds
            if (_backLabel != null)
            {
                var labelRect = _backLabel.GetGlobalRect();
                if (labelRect.HasPoint(mouseEvent.Position))
                {
                    OnBackClicked();
                }
            }
        }
    }
    
    private void OnNextClicked()
    {
        _currentTextIndex++;
        
        // If we've shown all messages, fade out TV, text, and buttons, then proceed to lobby
        if (_currentTextIndex >= _textMessages.Length)
        {
            GD.Print("All messages shown - fading out and transitioning to Lobby scene");
            
            // Create a tween to fade out the TV, text, and buttons
            var tween = CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(_tvLobby, "modulate:a", 0.0f, 0.5f);
            tween.TweenProperty(_infoTextLabel, "modulate:a", 0.0f, 0.5f);
            tween.TweenProperty(_nextLabel, "modulate:a", 0.0f, 0.5f);
            tween.TweenProperty(_backLabel, "modulate:a", 0.0f, 0.5f);
            tween.Chain().TweenCallback(Callable.From(() => 
            {
                GetTree().ChangeSceneToFile("res://scenes/Lobby.tscn");
            }));
        }
        else
        {
            // Otherwise, show the next message
            UpdateInfoText();
        }
    }
    
    private void OnBackClicked()
    {
        _currentTextIndex--;
        
        // If we're before the first message, go back to title scene
        if (_currentTextIndex < 0)
        {
            GD.Print("Going back to Title scene");
            GetTree().ChangeSceneToFile("res://scenes/TitleScene.tscn");
        }
        else
        {
            // Otherwise, show the previous message
            UpdateInfoText();
        }
    }
}
