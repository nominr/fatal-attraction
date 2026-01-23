using Godot;
using System;

/// <summary>
/// Goals menu that displays player objectives.
/// Can be shown at game start and reopened via button.
/// </summary>
public partial class GoalsMenu : PanelContainer
{
	[Signal]
	public delegate void MenuClosedEventHandler();

	private RichTextLabel _goalsText;
	private Button _closeButton;
	private string _currentRole = "";
	private string _loveInterest = "";
	private string _targets = "";

	public override void _Ready()
	{
		SetupUI();
		Visible = false;
	}

	private void SetupUI()
	{
		// Center the panel on screen
		CustomMinimumSize = new Vector2(600, 500);
		// Centered for 1200x800 world: (1200-600)/2 = 300, (800-500)/2 = 150
		Position = new Vector2(300, 150);
		
		// Main vertical container
		var mainVBox = new VBoxContainer();
		mainVBox.Name = "MainContainer";
		AddChild(mainVBox);

		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);
		mainVBox.AddChild(marginContainer);

		var contentVBox = new VBoxContainer();
		marginContainer.AddChild(contentVBox);

		// Title
		var titleLabel = new Label();
		titleLabel.Text = "YOUR GOALS";
		titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
		titleLabel.AddThemeFontSizeOverride("font_size", 28);
		contentVBox.AddChild(titleLabel);

		// Spacing
		var spacer = new Control();
		spacer.CustomMinimumSize = new Vector2(0, 20);
		contentVBox.AddChild(spacer);

		// Goals text area
		_goalsText = new RichTextLabel();
		_goalsText.BbcodeEnabled = true;
		_goalsText.FitContent = true;
		_goalsText.CustomMinimumSize = new Vector2(560, 350);
		contentVBox.AddChild(_goalsText);

		// Close button
		_closeButton = new Button();
		_closeButton.Text = "X CLOSE";
		_closeButton.CustomMinimumSize = new Vector2(150, 40);
		_closeButton.Pressed += OnClosePressed;
		contentVBox.AddChild(_closeButton);
	}

	private void OnClosePressed()
	{
		Hide();
		EmitSignal(SignalName.MenuClosed);
	}

	/// <summary>
	/// Set the player's role and display appropriate goals.
	/// </summary>
	public void SetRole(string role, string loveInterest = "", string targets = "")
	{
		_currentRole = role;
		_loveInterest = loveInterest;
		_targets = targets;
		UpdateGoalsDisplay();
	}

	private void UpdateGoalsDisplay()
	{
		string goalsContent = "";

		switch (_currentRole.ToLower())
		{
			case "admirer":
				goalsContent = "[center][font_size=24][b]ADMIRER[/b][/font_size][/center]\n\n";
				goalsContent += "[font_size=18][b]Goals:[/b][/font_size]\n";
				goalsContent += "• Max love meter with love interest\n";
				goalsContent += "• Kill three targets\n";
				goalsContent += "• Marry love interest\n\n";
				
				goalsContent += "[font_size=18][b]Love Interest:[/b][/font_size]\n";
				if (!string.IsNullOrEmpty(_loveInterest))
				{
					foreach (var name in _loveInterest.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
						goalsContent += $"• {name}\n";
					goalsContent += "\n";
				}
				else
					goalsContent += "• (None assigned)\n\n";
				
				goalsContent += "[font_size=18][b]Targets:[/b][/font_size]\n";
				if (!string.IsNullOrEmpty(_targets))
				{
					foreach (var name in _targets.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries))
						goalsContent += $"• {name}\n";
				}
				else
					goalsContent += "• (None assigned)\n";
				break;

			case "prophet":
				goalsContent = "[center][font_size=24][b]PROPHET[/b][/font_size][/center]\n\n";
				goalsContent += "[font_size=18][b]Goals:[/b][/font_size]\n";
				goalsContent += "• Convert people successfully\n";
				goalsContent += "• Max chaos meter through pranks and shenanigans\n";
				goalsContent += "• Achieve high chaos\n";
				break;

			case "producer":
				goalsContent = "[center][font_size=24][b]PRODUCER[/b][/font_size][/center]\n\n";
				goalsContent += "[font_size=18][b]Goals:[/b][/font_size]\n";
				goalsContent += "• Catch and identify the Admirer\n";
				goalsContent += "• Stop the Prophet from conversion and chaos\n";
				goalsContent += "• Max ratings meter\n";
				break;

			default:
				goalsContent = "[center]Waiting for role assignment...[/center]";
				break;
		}

		_goalsText.Text = goalsContent;
	}

	/// <summary>
	/// Show the goals menu.
	/// </summary>
	public void ShowMenu()
	{
		Show();
	}
}
