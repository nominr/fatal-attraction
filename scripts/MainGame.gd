extends Control

# Main game UI controller

@onready var game_engine: GameEngine = $GameEngine
@onready var goal_panel: PanelContainer = $GoalPanel
@onready var goal_text: RichTextLabel = $GoalPanel/MarginContainer/GoalText
@onready var turn_label: Label = $HUD/TurnLabel
@onready var role_label: Label = $HUD/RoleLabel
@onready var meters_container: VBoxContainer = $HUD/MetersContainer
@onready var npc_container: VBoxContainer = $NPCContainer
@onready var notification_panel: PanelContainer = $NotificationPanel
@onready var notification_text: RichTextLabel = $NotificationPanel/MarginContainer/NotificationText
@onready var start_button: Button = $GoalPanel/StartButton
@onready var editorial_panel: PanelContainer = $EditorialPanel
@onready var editorial_options: VBoxContainer = $EditorialPanel/MarginContainer/VBoxContainer/OptionsContainer

var current_npcs: Array = []
var game_started: bool = false
var marriage_modal: Control = null
var pending_marry_npc: GameEngine.NPC = null
var editorial_label: Label = null

# Networking
const USE_NETWORK: bool = true
var server_host: String = "127.0.0.1"  # Set to host laptop's LAN IP
var server_port: int = 3000
var network_client: NetworkClient = null
var current_role_net: String = ""

func _ready():
	start_button.pressed.connect(_on_start_button_pressed)
	game_engine.notification_added.connect(_on_notification_added)
	game_engine.meter_updated.connect(_on_meter_updated)
	game_engine.turn_started.connect(_on_turn_started)
	game_engine.turn_ended.connect(_on_turn_ended)

	if USE_NETWORK:
		network_client = NetworkClient.new()
		add_child(network_client)
		network_client.assigned_role.connect(_on_net_assigned_role)
		network_client.snapshot_received.connect(_on_net_snapshot)
		network_client.turn_started.connect(_on_net_turn_started)
		network_client.turn_ended.connect(_on_net_turn_ended)
		network_client.action_result.connect(_on_net_action_result)
		network_client.game_over.connect(_on_net_game_over)
	
	# Create editorial focus label
	editorial_label = Label.new()
	editorial_label.text = ""
	$HUD.add_child(editorial_label)
	var role_idx = $HUD.get_children().find(role_label)
	$HUD.move_child(editorial_label, role_idx + 1)
	
	# Show goals at start
	show_all_goals()
	npc_container.hide()
	notification_panel.hide()
	editorial_panel.hide()

func show_all_goals():
	var goals_text = "[center][b][color=gold]FATAL ATTRACTION - PLAYER GOALS[/color][/b][/center]\n\n"
	
	for role in ["admirer", "prophet", "producer"]:
		var goal_info = game_engine.get_player_goals(role)
		var role_config = game_engine.config.get("roles", {}).get(role, {})
		var win_condition = goal_info.get("win_condition", {})
		var primary = win_condition.get("primary", {})
		
		goals_text += "[b][color=cyan]%s[/color][/b]\n" % role.to_upper()
		goals_text += "[color=lightgreen]Powers:[/color]\n"
		
		var powers = role_config.get("powers", {})
		for power_id in powers:
			var power = powers[power_id]
			goals_text += "  • %s\n" % power.get("displayName", power_id)
		
		goals_text += "\n[color=yellow]Win Condition:[/color]\n"
		var requirement = primary.get("requirement", {})
		if requirement.has("meter"):
			goals_text += "  • Reach %s: %d/%d\n" % [
				requirement["meter"].to_upper(),
				requirement.get("minValue", 0),
				10
			]
		
		var goals = goal_info.get("goals", {})
		if not goals.is_empty():
			goals_text += "\n[color=lightblue]Goals:[/color]\n"
			for goal_id in goals:
				var goal = goals[goal_id]
				goals_text += "  • %s\n" % goal.get("displayName", goal_id)
		
		goals_text += "\n"
	
	goal_text.text = goals_text

func _on_start_button_pressed():
	game_started = true
	goal_panel.hide()
	npc_container.show()
	notification_panel.show()
	if USE_NETWORK:
		# Connect to LAN server
		var player_name = OS.get_unique_id()
		network_client.connect_to_server(server_host, server_port, player_name)
	else:
		start_new_turn()

func start_new_turn():
	if USE_NETWORK:
		# Wait for server turn_started events
		update_hud()
		editorial_panel.hide()
	else:
		game_engine.start_turn()
		update_hud()
		var npc_count = game_engine.config.get("gameRules", {}).get("npcsPerTurn", 3)
		current_npcs = game_engine.get_random_npcs(npc_count)
		display_npcs()
		editorial_panel.hide()

func update_hud():
	var role: String
	if USE_NETWORK:
		# turn label will be updated on turn_started
		var turn_val = game_engine.current_turn if game_engine.current_turn > 0 else 0
		turn_label.text = "Turn: %d/%d" % [turn_val, game_engine.max_turns]
		if current_role_net != "":
			role = current_role_net
		else:
			role = game_engine.get_current_role()
		role_label.text = "Current Player: %s" % role.to_upper()
	else:
		turn_label.text = "Turn: %d/%d" % [game_engine.current_turn, game_engine.max_turns]
		role = game_engine.get_current_role()
		role_label.text = "Current Player: %s" % role.to_upper()
	
	# Update editorial focus display
	var focus_text = ""
	if game_engine.editorial_focus != "":
		var focus_display = game_engine.editorial_focus.replace("_", " ").capitalize()
		if role == "admirer" and (game_engine.editorial_focus == "romantic_escalations" or game_engine.editorial_focus == "sudden_deaths"):
			focus_text = "📺 Editorial Focus: " + focus_display
		elif role == "prophet" and game_engine.editorial_focus == "chaos_spikes":
			focus_text = "📺 Editorial Focus: Chaos Spikes"
		elif game_engine.editorial_focus == "public_areas":
			focus_text = "📺 Editorial Focus: Public Areas"
	editorial_label.text = focus_text
	
	# Update meters
	for child in meters_container.get_children():
		child.queue_free()
	
	var player = game_engine.get_player_state(role)
	if player:
		for meter_name in player.meters:
			var meter = player.meters[meter_name]
			var meter_display = create_meter_display(meter_name, meter.value, meter.max_value)
			meters_container.add_child(meter_display)
	
	# Add targets remaining for Admirer
	if role == "admirer":
		var targets_remaining = 0
		for npc_id in game_engine.npcs:
			var npc = game_engine.npcs[npc_id]
			if npc.is_target and npc.alive:
				targets_remaining += 1
		
		var targets_label = Label.new()
		targets_label.text = "🎯 Targets Remaining: %d" % targets_remaining
		targets_label.add_theme_color_override("font_color", Color.ORANGE_RED)
		meters_container.add_child(targets_label)
	
	# Add converted count for Prophet
	if role == "prophet":
		var converted_npcs = game_engine.get_converted_npcs()
		var converted_label = Label.new()
		converted_label.text = "🔄 Converted: %d/2" % converted_npcs.size()
		converted_label.add_theme_color_override("font_color", Color.PURPLE)
		meters_container.add_child(converted_label)

func create_meter_display(meter_name: String, value: float, max_value: float) -> HBoxContainer:
	var container = HBoxContainer.new()
	
	var label = Label.new()
	label.text = "%s: " % meter_name.to_upper()
	label.custom_minimum_size = Vector2(100, 0)
	container.add_child(label)
	
	var progress = ProgressBar.new()
	progress.min_value = 0
	progress.max_value = max_value
	progress.value = value
	progress.custom_minimum_size = Vector2(200, 20)
	progress.show_percentage = false
	container.add_child(progress)
	
	var value_label = Label.new()
	value_label.text = " %.1f/%.0f" % [value, max_value]
	container.add_child(value_label)
	
	return container

func display_npcs():
	# Clear existing NPCs
	for child in npc_container.get_children():
		child.queue_free()
	
	var title = Label.new()
	title.text = "NPC Encounters"
	title.add_theme_font_size_override("font_size", 20)
	npc_container.add_child(title)
	
	# Add editorial attention button for Producer
	if game_engine.get_current_role() == "producer":
		var editorial_button = Button.new()
		editorial_button.text = "Adjust Editorial Attention"
		editorial_button.pressed.connect(show_editorial_attention)
		npc_container.add_child(editorial_button)
	
	if USE_NETWORK:
		for net_npc in current_npcs:
			var panel = PanelContainer.new()
			panel.custom_minimum_size = Vector2(600, 150)
			var vbox = VBoxContainer.new()
			panel.add_child(vbox)
			var header = HBoxContainer.new()
			vbox.add_child(header)
			var npc_name_label = Label.new()
			npc_name_label.text = String(net_npc.get("id", "NPC")).capitalize()
			npc_name_label.add_theme_font_size_override("font_size", 18)
			npc_name_label.add_theme_color_override("font_color", Color.WHITE)
			header.add_child(npc_name_label)
			var desc_label = Label.new()
			desc_label.text = String(net_npc.get("prompt", "An NPC appears."))
			desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			vbox.add_child(desc_label)
			var actions_label = Label.new()
			actions_label.text = "Actions:"
			actions_label.add_theme_font_size_override("font_size", 14)
			vbox.add_child(actions_label)
			var actions_container = HBoxContainer.new()
			vbox.add_child(actions_container)
			var role = current_role_net if current_role_net != "" else game_engine.get_current_role()
			var actions: Array = net_npc.get("actions", [])
			for a in actions:
				# Server sends unified actions; filter per role by checking availability client-side if needed
				var btn = Button.new()
				btn.text = String(a.get("text", "Action"))
				btn.pressed.connect(_on_action_pressed.bind(String(net_npc.get("id", "")), String(a.get("id", ""))))
				actions_container.add_child(btn)
			if actions.is_empty():
				var no_action_label = Label.new()
				no_action_label.text = "No actions available for your role."
				no_action_label.add_theme_color_override("font_color", Color.GRAY)
				actions_container.add_child(no_action_label)
			npc_container.add_child(panel)
	else:
		for npc in current_npcs:
			var npc_panel = create_npc_panel(npc)
			npc_container.add_child(npc_panel)

func create_npc_panel(npc: GameEngine.NPC) -> PanelContainer:
	var panel = PanelContainer.new()
	panel.custom_minimum_size = Vector2(600, 150)
	
	var vbox = VBoxContainer.new()
	panel.add_child(vbox)
	
	# NPC header
	var header = HBoxContainer.new()
	vbox.add_child(header)
	
	var npc_name_label = Label.new()
	npc_name_label.text = npc.npc_name
	npc_name_label.add_theme_font_size_override("font_size", 18)
	npc_name_label.add_theme_color_override("font_color", Color.WHITE)
	header.add_child(npc_name_label)
	
	# Show status
	if npc.is_love_interest and game_engine.get_current_role() == "admirer":
		var love_tag = Label.new()
		love_tag.text = " ❤️ LOVE INTEREST"
		love_tag.add_theme_color_override("font_color", Color.PINK)
		header.add_child(love_tag)
	
	if npc.is_target and game_engine.get_current_role() == "admirer":
		var target_tag = Label.new()
		target_tag.text = " 🎯 TARGET"
		target_tag.add_theme_color_override("font_color", Color.ORANGE_RED)
		header.add_child(target_tag)
	
	if npc.converted:
		var converted_tag = Label.new()
		converted_tag.text = " 🔄 CONVERTED"
		converted_tag.add_theme_color_override("font_color", Color.PURPLE)
		header.add_child(converted_tag)
	
	# NPC description
	var npc_config = game_engine.config.get("npcs", {}).get(npc.id, {})
	var interaction_tree = npc_config.get("interactionTree", {})
	var root = interaction_tree.get("root", {})
	var description = root.get("text", "An NPC appears.")
	
	var desc_label = Label.new()
	desc_label.text = description
	desc_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	vbox.add_child(desc_label)
	
	# Action buttons
	var actions_label = Label.new()
	actions_label.text = "Actions:"
	actions_label.add_theme_font_size_override("font_size", 14)
	vbox.add_child(actions_label)
	
	var actions_container = HBoxContainer.new()
	vbox.add_child(actions_container)
	
	var current_role = game_engine.get_current_role()
	var available_actions = game_engine.get_available_actions(npc.id, current_role)
	
	for action in available_actions:
		var button = Button.new()
		button.text = action.get("text", "Action")
		button.pressed.connect(_on_action_pressed.bind(npc.id, action.get("id", "")))
		actions_container.add_child(button)
	
	if available_actions.is_empty():
		var no_action_label = Label.new()
		no_action_label.text = "No actions available for your role."
		no_action_label.add_theme_color_override("font_color", Color.GRAY)
		actions_container.add_child(no_action_label)
	
	return panel

func _on_action_pressed(npc_id: String, action_id: String):
	var current_role: String
	if USE_NETWORK:
		current_role = current_role_net
	else:
		current_role = game_engine.get_current_role()
	var npc_config = game_engine.config.get("npcs", {}).get(npc_id, {})
	var interaction_tree = npc_config.get("interactionTree", {})
	var root = interaction_tree.get("root", {})
	var all_options = root.get("options", [])
	
	# Find the action
	var action = null
	for opt in all_options:
		if opt.get("id", "") == action_id:
			action = opt
			break
	
	if not action:
		return
	
	# Check if this is an admirer marry love action
	if action.get("marry_admirer_love", false):
		# Validate love meter is at max
		var player = game_engine.get_player_state(current_role)
		var love_meter = player.get_meter("love") if player else null
		
		if not love_meter or love_meter.value < 10.0:
			game_engine.add_notification("💔 You need maximum love (10/10) to marry Katy!")
			return
		
		# Perform the action to marry and emit notifications
		game_engine.perform_action(npc_id, action_id, current_role)
		
		# Check win condition
		var win_msg = game_engine.check_win_condition(current_role)
		if not win_msg.is_empty():
			show_game_over(win_msg)
			return
		
		# End turn after one action
		game_engine.end_turn()
		
		# Check if game is over
		if game_engine.is_game_over():
			show_game_over("GAME OVER - No winner by turn limit")
			return
		
		# Start next turn
		await get_tree().create_timer(0.5).timeout
		start_new_turn()
		return
	
	# Check if this is a marriage initiation action
	if action.get("marry_initiate", false):
		pending_marry_npc = game_engine.get_npc(npc_id)
		show_marriage_modal()
		return
	
	# Regular action
	if USE_NETWORK:
		network_client.perform_action(npc_id, action_id)
	else:
		game_engine.perform_action(npc_id, action_id, current_role)
	
	# Check win condition
	var win_msg = game_engine.check_win_condition(current_role)
	if not win_msg.is_empty():
		show_game_over(win_msg)
		return
	
	# End turn after one action
	if not USE_NETWORK:
		game_engine.end_turn()
	
	# Check if game is over
	if game_engine.is_game_over():
		show_game_over("GAME OVER - No winner by turn limit")
		return
	
	# Start next turn
	if not USE_NETWORK:
		await get_tree().create_timer(0.5).timeout
		start_new_turn()

# Networking signal handlers
func _on_net_assigned_role(role: String) -> void:
	current_role_net = role

func _on_net_snapshot(state: Dictionary) -> void:
	# Update meters from snapshot
	var players: Dictionary = state.get("players", {})
	for role in players.keys():
		var player = game_engine.get_player_state(role)
		if player:
			var meters: Dictionary = players[role].get("meters", {})
			for m in meters.keys():
				if player.meters.has(m):
					player.meters[m].value = float(meters[m].get("value", player.meters[m].value))
					player.meters[m].max_value = float(meters[m].get("max", player.meters[m].max_value))
	update_hud()

func _on_net_turn_started(data: Dictionary) -> void:
	var turn = int(data.get("turn", game_engine.current_turn))
	game_engine.current_turn = turn
	current_role_net = String(data.get("active_role", current_role_net))
	turn_label.text = "Turn: %d/%d" % [game_engine.current_turn, game_engine.max_turns]
	role_label.text = "Current Player: %s" % current_role_net.to_upper()
	current_npcs = Array(data.get("npcs", []))
	display_npcs()

func _on_net_turn_ended(_data: Dictionary) -> void:
	# Clear NPCs UI until next turn
	for child in npc_container.get_children():
		child.queue_free()

func _on_net_action_result(data: Dictionary) -> void:
	# Notifications
	var notifs: Array = Array(data.get("notifications", []))
	for n in notifs:
		_on_notification_added(String(n))
	# Meter updates
	var mus: Array = Array(data.get("meter_updates", []))
	for u in mus:
		var role = String(u.get("role", ""))
		var meter_name = String(u.get("meter", ""))
		var value = float(u.get("value", 0.0))
		var maxv = float(u.get("max", 10.0))
		var player = game_engine.get_player_state(role)
		if player and player.meters.has(meter_name):
			player.meters[meter_name].value = value
			player.meters[meter_name].max_value = maxv
	update_hud()

func _on_net_game_over(message: String) -> void:
	show_game_over(message)

func show_editorial_attention():
	# Create a semi-transparent background that blocks clicks
	var background = ColorRect.new()
	background.color = Color.BLACK
	background.color.a = 0.5
	background.anchors_preset = 15
	background.anchor_right = 1.0
	background.anchor_bottom = 1.0
	background.z_index = 999
	background.mouse_filter = Control.MOUSE_FILTER_STOP
	background.name = "EditorialBackground"
	add_child(background)
	
	# Ensure editorial panel is above the background
	editorial_panel.z_index = 1000
	move_child(editorial_panel, -1)  # Move to end (top of draw order)
	editorial_panel.show()
	
	# Clear existing options
	for child in editorial_options.get_children():
		child.queue_free()
	
	var title = Label.new()
	title.text = "Choose Editorial Focus:"
	title.add_theme_font_size_override("font_size", 16)
	editorial_options.add_child(title)
	
	var focus_config = game_engine.config.get("systems", {}).get("editorial_attention", {})
	var focus_opts = focus_config.get("focusOptions", {})
	
	for focus_id in focus_opts:
		var focus = focus_opts[focus_id]
		var button = Button.new()
		button.text = focus.get("displayName", focus_id)
		button.pressed.connect(_on_editorial_focus_selected.bind(focus_id))
		editorial_options.add_child(button)
	
	# Add report admirer button if condition met
	if game_engine.admirer_can_be_reported:
		var report_button = Button.new()
		report_button.text = "Report Admirer"
		report_button.pressed.connect(_on_report_admirer)
		editorial_options.add_child(report_button)

func _on_editorial_focus_selected(focus_id: String):
	game_engine.editorial_focus = focus_id
	game_engine.add_notification("📺 Producer focused on: %s" % focus_id.replace("_", " ").capitalize())

	# Apply ratings change for chaos/romance focuses (50/50 chance +2 or -1)
	if focus_id == "chaos_spikes" or focus_id == "romantic_escalations":
		var producer = game_engine.get_player_state("producer")
		if producer and producer.meters.has("ratings"):
			var delta = 2 if randf() < 0.5 else -1
			producer.meters["ratings"].add(delta)
			var verb = "raised" if delta > 0 else "lowered"
			game_engine.add_notification("[PRODUCER] %s ratings by %d (now %.1f/%.0f)" % [
				verb, delta, producer.meters["ratings"].value, producer.meters["ratings"].max_value
			])
			game_engine.meter_updated.emit("producer", "ratings", producer.meters["ratings"].value, producer.meters["ratings"].max_value)

	editorial_panel.hide()

	# Remove background
	for child in get_children():
		if child.name == "EditorialBackground":
			child.queue_free()

func _on_report_admirer():
	game_engine.admirer_reported = true
	game_engine.admirer_can_be_reported = false
	game_engine.add_notification("📺 Producer reported the Admirer! Admirer becomes an observer.")
	editorial_panel.hide()
	
	# Remove background
	for child in get_children():
		if child.name == "EditorialBackground":
			child.queue_free()

func show_marriage_modal():
	# Create a semi-transparent background
	var background = ColorRect.new()
	background.color = Color.BLACK
	background.color.a = 0.5
	background.anchors_preset = 15
	background.anchor_right = 1.0
	background.anchor_bottom = 1.0
	background.z_index = 999
	add_child(background)
	
	# Create the modal panel
	marriage_modal = PanelContainer.new()
	marriage_modal.z_index = 1000
	marriage_modal.anchors_preset = 8
	marriage_modal.anchor_left = 0.5
	marriage_modal.anchor_top = 0.5
	marriage_modal.anchor_right = 0.5
	marriage_modal.anchor_bottom = 0.5
	marriage_modal.offset_left = -250.0
	marriage_modal.offset_top = -200.0
	marriage_modal.offset_right = 250.0
	marriage_modal.offset_bottom = 200.0
	add_child(marriage_modal)
	
	var margin = MarginContainer.new()
	margin.add_theme_constant_override("margin_left", 20)
	margin.add_theme_constant_override("margin_top", 20)
	margin.add_theme_constant_override("margin_right", 20)
	margin.add_theme_constant_override("margin_bottom", 20)
	marriage_modal.add_child(margin)
	
	var vbox = VBoxContainer.new()
	margin.add_child(vbox)
	
	var title = Label.new()
	title.text = "Who should %s marry?" % pending_marry_npc.npc_name
	title.add_theme_font_size_override("font_size", 16)
	vbox.add_child(title)
	
	var options_label = Label.new()
	options_label.text = "Choose a partner:"
	vbox.add_child(options_label)
	
	var marriageable = game_engine.get_marriageable_npcs(pending_marry_npc.id)
	
	if marriageable.is_empty():
		var no_option = Label.new()
		no_option.text = "No available NPCs to marry."
		no_option.add_theme_color_override("font_color", Color.GRAY)
		vbox.add_child(no_option)
	else:
		for target_npc in marriageable:
			var button = Button.new()
			button.text = target_npc.npc_name
			button.pressed.connect(_on_marriage_selected.bind(pending_marry_npc.id, target_npc.id))
			vbox.add_child(button)
	
	var cancel_button = Button.new()
	cancel_button.text = "Cancel"
	cancel_button.pressed.connect(_on_marriage_cancelled)
	vbox.add_child(cancel_button)

func _on_marriage_selected(npc1_id: String, npc2_id: String):
	var npc1 = game_engine.get_npc(npc1_id)
	var npc2 = game_engine.get_npc(npc2_id)
	
	if npc1 and npc2:
		# Mark both as married to each other
		npc1.married = true
		npc1.married_to = npc2_id
		npc2.married = true
		npc2.married_to = npc1_id
		
		game_engine.add_notification("💒 %s and %s are now married!" % [npc1.npc_name, npc2.npc_name])
	
	_cleanup_marriage_modal()
	_finish_marriage_action()

func _on_marriage_cancelled():
	_cleanup_marriage_modal()
	# Don't finish the action - let them try again

func _cleanup_marriage_modal():
	if marriage_modal and is_instance_valid(marriage_modal):
		marriage_modal.queue_free()
	
	# Clean up background
	for child in get_children():
		if child is ColorRect and child.color == Color(0, 0, 0, 0.5):
			child.queue_free()
	
	pending_marry_npc = null

func _finish_marriage_action():
	var current_role = game_engine.get_current_role()
	var player = game_engine.get_player_state(current_role)
	if player and player.meters.has("ratings"):
		var meter = player.meters["ratings"]
		meter.add(1)
		var rating_val = meter.value
		var rating_max = meter.max_value
		game_engine.add_notification("[PRODUCER] raised ratings to %.1f/%.0f" % [rating_val, rating_max])
		game_engine.meter_updated.emit(current_role, "ratings", rating_val, rating_max)
	
	# Check win condition
	var win_msg = game_engine.check_win_condition(current_role)
	if not win_msg.is_empty():
		show_game_over(win_msg)
		return
	
	# End turn after marriage
	game_engine.end_turn()
	
	# Check if game is over
	if game_engine.is_game_over():
		show_game_over("GAME OVER - No winner by turn limit")
		return
	
	# Start next turn
	await get_tree().create_timer(0.5).timeout
	start_new_turn()

func _on_notification_added(message: String):
	var current_text = notification_text.text
	notification_text.text = current_text + message + "\n"
	
	# Auto-scroll to bottom
	await get_tree().create_timer(0.1).timeout
	if notification_text.get_v_scroll_bar():
		notification_text.get_v_scroll_bar().value = notification_text.get_v_scroll_bar().max_value

func _on_meter_updated(player_role: String, meter_name: String, value: float, max_value: float):
	update_hud()

func _on_turn_started(turn: int, player_role: String):
	pass

func _on_turn_ended(turn: int):
	# Check if game is over
	if game_engine.is_game_over():
		show_game_over("GAME OVER - No winner by turn limit")
		return
	
	# Show "Next Turn" button
	await get_tree().create_timer(1.0).timeout
	start_new_turn()

func show_game_over(message: String):
	var game_over_dialog = AcceptDialog.new()
	game_over_dialog.title = "Game Over"
	game_over_dialog.dialog_text = message + "\n\n" + get_final_summary()
	game_over_dialog.confirmed.connect(func(): get_tree().reload_current_scene())
	add_child(game_over_dialog)
	game_over_dialog.popup_centered()

func get_final_summary() -> String:
	var summary = "Final Scores:\n\n"
	var status = game_engine.get_game_status()
	var players = status.get("players", {})
	
	for role in players:
		var player_data = players[role]
		var meters = player_data.get("meters", {})
		summary += "%s:\n" % role.to_upper()
		for meter_name in meters:
			var meter_data = meters[meter_name]
			summary += "  %s: %.1f/%.0f\n" % [
				meter_name,
				meter_data.get("value", 0),
				meter_data.get("max", 10)
			]
		summary += "\n"
	
	return summary

func _on_end_turn_button_pressed():
	game_engine.end_turn()
