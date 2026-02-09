using Godot;
using System;

public partial class MoneyGameOverlay : Control
{
    [Signal]
    public delegate void ActionSelectedEventHandler(string actionId);

    private Label _targetLabel;
    private Label _currentLabel;
    private Label _resultLabel;
    private HBoxContainer _coinContainer;
    private Button _submitButton;
    private Panel _panel;

    private int _targetSum;
    private int _currentSum;

    public override void _Ready()
    {
        // Setup UI
        this.SetAnchorsPreset(LayoutPreset.Center);
        this.Size = new Vector2(400, 300);
        this.Visible = false;

        _panel = new Panel();
        _panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(LayoutPreset.FullRect);
        vbox.AddThemeConstantOverride("separation", 20);
        vbox.Alignment = BoxContainer.AlignmentMode.Center;
        _panel.AddChild(vbox);

        var title = new Label();
        title.Text = "MONEY GAME";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 24);
        vbox.AddChild(title);

        _targetLabel = new Label();
        _targetLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_targetLabel);

        _currentLabel = new Label();
        _currentLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _currentLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        vbox.AddChild(_currentLabel);

        _coinContainer = new HBoxContainer();
        _coinContainer.Alignment = BoxContainer.AlignmentMode.Center;
        _coinContainer.AddThemeConstantOverride("separation", 15);
        vbox.AddChild(_coinContainer);

        AddCoinButton(1);
        AddCoinButton(5);
        AddCoinButton(10);

        _submitButton = new Button();
        _submitButton.Text = "SUBMIT OFFER";
        _submitButton.CustomMinimumSize = new Vector2(150, 40);
        _submitButton.Pressed += () => EmitSignal(SignalName.ActionSelected, "money_submit");
        _submitButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        vbox.AddChild(_submitButton);

        _resultLabel = new Label();
        _resultLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_resultLabel);
    }

    private void AddCoinButton(int value)
    {
        var btn = new Button();
        btn.Text = $"+{value}";
        btn.CustomMinimumSize = new Vector2(50, 50);
        btn.Pressed += () => EmitSignal(SignalName.ActionSelected, $"money_add_{value}");
        _coinContainer.AddChild(btn);
    }

    public void UpdateState(int target, int current)
    {
        _targetSum = target;
        _currentSum = current;

        _targetLabel.Text = $"TARGET SUM: {target}";
        _currentLabel.Text = $"CURRENT OFFER: {current}";
        
        // Visual feedback
        if (current == target) _currentLabel.AddThemeColorOverride("font_color", Colors.Green);
        else if (current > target) _currentLabel.AddThemeColorOverride("font_color", Colors.Red);
        else _currentLabel.AddThemeColorOverride("font_color", Colors.Yellow);
    }

    public void ShowGame()
    {
        Visible = true;
    }

    public void HideGame()
    {
        Visible = false;
    }
}
