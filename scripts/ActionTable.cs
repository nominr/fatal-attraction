using Godot;
using System;

public partial class ActionTable : Control
{
    private TextureRect _backgroundPanel;
    private Label _winConditionsLabel;
    private GridContainer _grid;
    private Font _customFont;
    private ColorRect _backgroundDim;

    // Colors
    private readonly Color _prophetColor = new Color("A020F0"); // Purple
    private readonly Color _admirerColor = new Color("FF69B4"); // Hot Pink
    private readonly Color _producerColor = new Color("00FF00"); // Lime Green

    public override void _Ready()
    {
        // Load Resources
        _customFont = ResourceLoader.Load<Font>("res://assets/Pixer-Regular.otf");
        var bgTexture = ResourceLoader.Load<Texture2D>("res://assets/ai_goals_box.png");

        // Full screen overlay
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        
        // Background Dim
        _backgroundDim = new ColorRect();
        _backgroundDim.Color = new Color(0, 0, 0, 0.6f);
        _backgroundDim.SetAnchorsPreset(LayoutPreset.FullRect);
        _backgroundDim.MouseFilter = MouseFilterEnum.Stop;
        AddChild(_backgroundDim);

        // Dynamic Scale - 852
        var viewportSize = GetViewportRect().Size;
        float targetHeight = 852; 
        float aspectRatio = 1.0f;
        if (bgTexture != null)
        {
            aspectRatio = (float)bgTexture.GetWidth() / (float)bgTexture.GetHeight();
        }
        float panelWidth = targetHeight * aspectRatio;
        float panelHeight = targetHeight;

        // Main Panel
        _backgroundPanel = new TextureRect();
        _backgroundPanel.Texture = bgTexture;
        _backgroundPanel.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        _backgroundPanel.StretchMode = TextureRect.StretchModeEnum.Scale;
        _backgroundPanel.Size = new Vector2(panelWidth, panelHeight);
        
        float centeredY = (viewportSize.Y - panelHeight) / 2f;
        float shiftAmount = viewportSize.Y * 0.08f; 
        _backgroundPanel.Position = new Vector2(
            (viewportSize.X - panelWidth) / 2f,
            centeredY + shiftAmount
        );
        AddChild(_backgroundPanel);

        // Header Text - Reduced by 30% from 42px (42 * 0.7 = 29)
        var subtitleLabel = new Label();
        subtitleLabel.Text = "Master the contestants of the Villa with your unique toolkit.";
        subtitleLabel.AddThemeFontOverride("font", _customFont);
        subtitleLabel.AddThemeFontSizeOverride("font_size", 29); 
        subtitleLabel.AddThemeColorOverride("font_color", Colors.White);
        subtitleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        subtitleLabel.Size = new Vector2(panelWidth - 100, 60); // Narrower to confine to box
        subtitleLabel.Position = new Vector2(50, 140); 
        _backgroundPanel.AddChild(subtitleLabel);

        // Win Conditions Label - Moved UP below header and made BIG (24px)
        _winConditionsLabel = new Label();
        _winConditionsLabel.Text = "WIN: 50% NPS OR MOST INFLUENCE BY TIME OUT";
        _winConditionsLabel.AddThemeFontOverride("font", _customFont);
        _winConditionsLabel.AddThemeFontSizeOverride("font_size", 24); 
        _winConditionsLabel.AddThemeColorOverride("font_color", Colors.Yellow);
        _winConditionsLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _winConditionsLabel.Size = new Vector2(panelWidth - 100, 40);
        _winConditionsLabel.Position = new Vector2(50, 185); 
        _backgroundPanel.AddChild(_winConditionsLabel);

        // Grid Container
        _grid = new GridContainer();
        _grid.Columns = 4;
        _grid.AddThemeConstantOverride("h_separation", 8); 
        _grid.AddThemeConstantOverride("v_separation", 8);
        
        BuildTable();

        // Wrap grid in a CenterContainer - Pushed DOWN to accommodate new header/win layout
        var gridWrapper = new CenterContainer();
        float gridTop = 260; 
        float gridBottom = 120;
        gridWrapper.Position = new Vector2(0, gridTop);
        gridWrapper.Size = new Vector2(panelWidth, panelHeight - gridTop - gridBottom);
        gridWrapper.AddChild(_grid);
        _backgroundPanel.AddChild(gridWrapper);

        // Close Button (X) - Moved slightly lower
        var closeBtn = new Button();
        closeBtn.Text = "CLOSE DASHBOARD [X]";
        closeBtn.AddThemeFontOverride("font", _customFont);
        closeBtn.AddThemeFontSizeOverride("font_size", 20);
        closeBtn.CustomMinimumSize = new Vector2(240, 50); 
        closeBtn.Position = new Vector2((panelWidth - 240) / 2f, panelHeight - 80); 
        closeBtn.Pressed += Hide;
        var closeBtnStyle = new StyleBoxFlat();
        closeBtnStyle.BgColor = new Color(0.4f, 0, 0, 0.8f);
        closeBtnStyle.SetCornerRadiusAll(6);
        closeBtn.AddThemeStyleboxOverride("normal", closeBtnStyle);
        _backgroundPanel.AddChild(closeBtn);

        Visible = false;
    }

    private void BuildTable()
    {
        // --- Headers ---
        AddHeaderCell("Action Type", Colors.White);
        AddHeaderCell("PROPHET", _prophetColor);
        AddHeaderCell("ADMIRER", _admirerColor);
        AddHeaderCell("PRODUCER", _producerColor);

        // --- Row 1: Contestant Interactions ---
        AddRowHeader("Contestant\nInteractions");
        AddActionCell(null, "Convert:\n+CHAOS", _prophetColor);
        AddActionCell(null, "Flirt:\n+LOVE", _admirerColor);
        AddActionCell(null, "Money:\n+APPEAL", _producerColor);

        // --- Row 2: Physical Interactions ---
        AddRowHeader("Physical\nInteractions");
        AddActionCell(null, "Punch:\nStun", _prophetColor);
        AddActionCell(null, "Punch:\nStun", _admirerColor);
        AddActionCell(null, "Punch:\nStun", _producerColor);

        // --- Row 3: Special Item ---
        AddRowHeader("Special\nItem");
        AddActionCell(null, "Token of\nReincarnation", _prophetColor);
        AddActionCell(null, "Knife", _admirerColor);
        AddActionCell(null, "Security\nCameras", _producerColor);
    }

    private void AddHeaderCell(string text, Color color)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontOverride("font", _customFont);
        label.AddThemeFontSizeOverride("font_size", 14); 
        label.AddThemeColorOverride("font_color", color);
        
        var wrapper = new PanelContainer();
        var style = new StyleBoxFlat();
        style.DrawCenter = false;
        style.BorderColor = color;
        style.SetBorderWidthAll(1); 
        style.ContentMarginLeft = 5;
        style.ContentMarginRight = 5;
        wrapper.AddThemeStyleboxOverride("panel", style);
        wrapper.AddChild(label);
        
        wrapper.CustomMinimumSize = new Vector2(130, 40); 
        _grid.AddChild(wrapper);
    }

    private void AddRowHeader(string text)
    {
        var label = new Label();
        label.Text = text;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.AddThemeFontOverride("font", _customFont);
        label.AddThemeFontSizeOverride("font_size", 12); 
        label.AddThemeColorOverride("font_color", Colors.White);
        
        var panel = new PanelContainer();
        var style = new StyleBoxFlat();
        style.DrawCenter = false;
        style.BorderColor = new Color(0.7f, 0.7f, 0.7f, 0.5f);
        style.SetBorderWidthAll(1);
        panel.AddThemeStyleboxOverride("panel", style);
        panel.AddChild(label);
        
        panel.CustomMinimumSize = new Vector2(90, 66); 
        _grid.AddChild(panel);
    }

    private void AddActionCell(string iconPath, string text, Color color)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat();
        style.DrawCenter = false;
        style.BorderColor = color.Lerp(Colors.White, 0.2f);
        style.SetBorderWidthAll(1);
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        panel.AddThemeStyleboxOverride("panel", style);

        var hbox = new HBoxContainer();
        hbox.Alignment = BoxContainer.AlignmentMode.Center;
        hbox.AddThemeConstantOverride("separation", 8);
        
        var label = new Label();
        label.Text = text;
        label.AddThemeFontOverride("font", _customFont);
        label.AddThemeFontSizeOverride("font_size", 12); 
        label.AddThemeColorOverride("font_color", Colors.White);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        hbox.AddChild(label);

        panel.AddChild(hbox);
        panel.CustomMinimumSize = new Vector2(130, 66);
        _grid.AddChild(panel);
    }
}
