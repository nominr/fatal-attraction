extends Node
class_name GameEngine

# Core game state management for Fatal Attraction

signal notification_added(message: String)
signal meter_updated(player_role: String, meter_name: String, value: float, max_value: float)
signal game_state_changed()
signal turn_started(turn: int, player_role: String)
signal turn_ended(turn: int)

var config: Dictionary = {}
var current_turn: int = 1
var max_turns: int = 15  # Increased from 10
var current_player_idx: int = 0
var players: Dictionary = {}  # Role -> PlayerState
var npcs: Dictionary = {}  # ID -> NPC
var notifications: Array = []
var editorial_focus: String = ""
var admirer_can_be_reported: bool = false
var admirer_reported: bool = false

const ROLES = ["admirer", "prophet", "producer"]

class Meter:
	var name: String
	var value: float = 0.0
	var min_value: float = 0.0
	var max_value: float = 10.0
	
	func _init(p_name: String, start: float = 0.0, min_val: float = 0.0, max_val: float = 10.0):
		name = p_name
		value = start
		min_value = min_val
		max_value = max_val
	
	func add(delta: float) -> float:
		value = clampf(value + delta, min_value, max_value)
		return value
	
	func to_dict() -> Dictionary:
		return {
			"name": name,
			"value": value,
			"min": min_value,
			"max": max_value
		}

class NPC:
	var id: String
	var npc_name: String
	var converted: bool = false
	var alive: bool = true
	var is_love_interest: bool = false
	var is_target: bool = false  # For Admirer's kill targets
	var married: bool = false  # For Producer marriages
	var married_to: String = ""  # ID of NPC this one is married to
	
	func _init(p_id: String, p_name: String, p_love_interest: bool = false, p_target: bool = false):
		id = p_id
		npc_name = p_name
		is_love_interest = p_love_interest
		is_target = p_target
	
	func to_dict() -> Dictionary:
		return {
			"id": id,
			"name": npc_name,
			"converted": converted,
			"alive": alive,
			"is_love_interest": is_love_interest,
			"is_target": is_target,
			"married": married,
			"married_to": married_to
		}

class PlayerState:
	var role: String
	var meters: Dictionary = {}  # String -> Meter
	var actions_taken: Array = []
	var goals_met: Array = []
	
	func _init(p_role: String):
		role = p_role
	
	func get_meter(meter_name: String) -> Meter:
		return meters.get(meter_name, null)
	
	func to_dict() -> Dictionary:
		var meters_dict = {}
		for meter_name in meters:
			meters_dict[meter_name] = meters[meter_name].to_dict()
		
		return {
			"role": role,
			"meters": meters_dict,
			"actions_taken": actions_taken,
			"goals_met": goals_met
		}

func _ready():
	load_config()
	initialize_game()

func load_config():
	var file = FileAccess.open("res://data/game_configuration.json", FileAccess.READ)
	if file:
		var json_text = file.get_as_text()
		file.close()
		
		var json = JSON.new()
		var parse_result = json.parse(json_text)
		if parse_result == OK:
			config = json.data
			max_turns = config.get("gameRules", {}).get("turnsPerGame", 15)
		else:
			push_error("Failed to parse game configuration JSON")
	else:
		push_error("Failed to load game configuration file")

func initialize_game():
	# Initialize players
	for role_name in ROLES:
		var player = PlayerState.new(role_name)
		var role_config = config.get("roles", {}).get(role_name, {})
		var meters_config = role_config.get("meters", {})
		
		for meter_name in meters_config:
			var meter_config = meters_config[meter_name]
			var meter = Meter.new(
				meter_name,
				meter_config.get("start", 0.0),
				meter_config.get("min", 0.0),
				meter_config.get("max", 10.0)
			)
			player.meters[meter_name] = meter
		
		players[role_name] = player
	
	# Initialize NPCs
	var npcs_config = config.get("npcs", {})
	for npc_id in npcs_config:
		var npc_config = npcs_config[npc_id]
		var npc = NPC.new(
			npc_id,
			npc_config.get("name", npc_id),
			npc_config.get("role", "") == "love_interest",
			npc_config.get("isTarget", false)
		)
		npc.converted = npc_config.get("convertedStatus", false)
		npc.married = npc_config.get("married", false)
		npcs[npc_id] = npc
	
	game_state_changed.emit()

func get_player_state(role: String) -> PlayerState:
	return players.get(role, null)

func get_npc(npc_id: String) -> NPC:
	return npcs.get(npc_id, null)

func get_alive_npcs() -> Array:
	var alive = []
	for npc_id in npcs:
		var npc = npcs[npc_id]
		if npc.alive:
			alive.append(npc)
	return alive

func get_converted_npcs() -> Array:
	var converted = []
	for npc_id in npcs:
		var npc = npcs[npc_id]
		if npc.converted and npc.alive:
			converted.append(npc)
	return converted

func get_marriageable_npcs(exclude_npc_id: String = "") -> Array:
	"""Get NPCs that can be married (not love interest, not married, not converted, not the excluded one)"""
	var marriageable = []
	for npc_id in npcs:
		var npc = npcs[npc_id]
		if npc.alive and not npc.is_love_interest and not npc.married and not npc.converted and npc_id != exclude_npc_id:
			marriageable.append(npc)
	return marriageable

func get_random_npcs(count: int) -> Array:
	var alive = get_alive_npcs()
	if alive.is_empty():
		return []
	
	alive.shuffle()
	return alive.slice(0, min(count, alive.size()))

func add_notification(message: String):
	notifications.append(message)
	notification_added.emit(message)

func get_and_clear_notifications() -> Array:
	var notifs = notifications.duplicate()
	notifications.clear()
	return notifs

func advance_turn() -> bool:
	if current_turn < max_turns:
		current_turn += 1
		return true
	return false

func is_game_over() -> bool:
	return current_turn >= max_turns

func get_current_role() -> String:
	return ROLES[current_player_idx]

func next_player():
	current_player_idx = (current_player_idx + 1) % ROLES.size()

func start_turn():
	var role = get_current_role()
	var player = get_player_state(role)
	
	add_notification("\n--- TURN %d: %s ---" % [current_turn, role.to_upper()])
	
	# Show current meters
	for meter_name in player.meters:
		var meter = player.meters[meter_name]
		add_notification("  %s: %.1f/%.0f" % [meter_name.to_upper(), meter.value, meter.max_value])
	
	# Notify about editorial focus if relevant
	if editorial_focus != "":
		var focus_display = editorial_focus.replace("_", " ").capitalize()
		if role == "admirer" and (editorial_focus == "romantic_escalations" or editorial_focus == "sudden_deaths"):
			add_notification("📺 Producer is focusing on " + focus_display + "!")
		elif role == "prophet" and editorial_focus == "chaos_spikes":
			add_notification("📺 Producer is focusing on chaos spikes!")
		elif editorial_focus == "public_areas":
			add_notification("📺 Producer is focusing on public areas!")
	
	turn_started.emit(current_turn, role)

func end_turn():
	add_notification("--- TURN %d END ---\n" % current_turn)
	turn_ended.emit(current_turn)
	next_player()
	
	# Check if round is complete (all players went)
	if current_player_idx == 0:
		advance_turn()

func get_available_actions(npc_id: String, player_role: String) -> Array:
	var npc_config = config.get("npcs", {}).get(npc_id, {})
	if npc_config.is_empty():
		return []
	
	var npc = get_npc(npc_id)
	if not npc or not npc.alive:
		return []
	
	var interaction_tree = npc_config.get("interactionTree", {})
	var root = interaction_tree.get("root", {})
	var all_options = root.get("options", [])
	
	var available = []
	for option in all_options:
		if is_option_available(option, player_role, npc):
			available.append(option)
	
	return available

func is_option_available(option: Dictionary, player_role: String, npc: NPC) -> bool:
	var requires = option.get("requires", {})
	
	# Check role requirement
	if requires.has("role") and requires["role"] != player_role:
		return false
	
	# Check NPC status requirement
	if requires.has("npcStatus"):
		var status = requires["npcStatus"]
		if status == "converted" and not npc.converted:
			return false
		if status == "nonConverted" and npc.converted:
			return false
	
	# Check marriage requirement (can only marry once)
	if requires.has("npcMarried"):
		var can_marry = not requires["npcMarried"]
		if not can_marry and npc.married:
			return false
	
	# Check marriageable requirement (has available NPCs to marry to)
	if requires.has("npcMarriageable"):
		# Can't initiate marriage if this NPC is already married
		if npc.married:
			return false
		# Can't marry if this NPC is converted
		if npc.converted:
			return false
		# Can't marry if no available partners
		if get_marriageable_npcs(npc.id).is_empty():
			return false
	
	# If this is a convert action, check NPC is not married
	var action_id = option.get("id", "")
	if action_id.begins_with("convert_") or action_id == "convert":
		if npc.married:
			return false
	
	return true

func perform_action(npc_id: String, action_id: String, player_role: String) -> bool:
	var npc_config = config.get("npcs", {}).get(npc_id, {})
	if npc_config.is_empty():
		return false
	
	var interaction_tree = npc_config.get("interactionTree", {})
	var root = interaction_tree.get("root", {})
	var all_options = root.get("options", [])
	
	var option = null
	for opt in all_options:
		if opt.get("id", "") == action_id:
			option = opt
			break
	
	if not option:
		return false
	
	# Check for admirer kill under editorial focus
	if action_id in ["kill", "kill_rebecca"] and player_role == "admirer" and editorial_focus != "":
		admirer_can_be_reported = true
	
	# Resolve action
	var success = true
	var resolution = option.get("resolution", {})
	var res_type = resolution.get("type", "immediate")
	
	if res_type == "chance":
		var success_chance = resolution.get("successChance", 0.5)
		success = randf() < success_chance
		add_notification("🎲 Chance Roll: %s" % ("✅ Success!" if success else "❌ Failed!"))
	elif res_type == "rps":
		success = randf() < 0.5
		add_notification("✋ Rock-Paper-Scissors: %s" % ("You won!" if success else "You lost!"))
	
	# Apply effects
	var effects_key = "effects_on_success" if success else "effects_on_failure"
	var effects = option.get(effects_key, option.get("effects", []))
	
	for effect in effects:
		apply_effect(effect, player_role, npc_id)
	
	return success

func apply_effect(effect: Dictionary, player_role: String, npc_id: String):
	# Meter effect
	if effect.has("meter"):
		var meter_name = effect["meter"]
		var delta = effect.get("delta", 0.0)
		var player = get_player_state(player_role)
		
		if player and player.meters.has(meter_name):
			var meter = player.meters[meter_name]
			meter.add(delta)
			var verb = "raised" if delta > 0 else "lowered"
			add_notification("[%s] %s %s to %.1f/%.0f" % [
				player_role.to_upper(), verb, meter_name, meter.value, meter.max_value
			])
			meter_updated.emit(player_role, meter_name, meter.value, meter.max_value)
	
	# Notification effect
	if effect.has("notification"):
		add_notification(effect["notification"])
	
	# NPC field effect
	if effect.has("npc") and effect["npc"] == npc_id:
		var npc = get_npc(npc_id)
		if npc:
			var field = effect.get("field", "")
			var value = effect.get("value")
			
			if field == "convertedStatus":
				npc.converted = value
				# Notify conversion status
				add_notification("🔄 %s is now %s" % [
					npc.npc_name,
					"CONVERTED" if value else "UNCONVERTED"
				])
			elif field == "alive":
				npc.alive = value
				if not value:
					add_notification("💀 %s has been eliminated" % npc.npc_name)
			elif field == "married":
				npc.married = value
				if value:
					add_notification("💒 %s is now married" % npc.npc_name)

func check_win_condition(player_role: String) -> String:
	var player = get_player_state(player_role)
	if not player:
		return ""
	
	var win_config = config.get("gameRules", {}).get("winConditions", {}).get(player_role, {})
	var primary = win_config.get("primary", {})
	var requirement = primary.get("requirement", {})
	
	# Check for prophet - requires both chaos at max AND 2+ converted NPCs
	if player_role == "prophet" and primary.get("goal") == "chaos_and_converts":
		var chaos_meter = player.get_meter("chaos")
		if chaos_meter and chaos_meter.value >= 10.0:
			# Count converted NPCs
			var converted_count = 0
			for npc_id in npcs:
				var npc = npcs[npc_id]
				if npc.converted:
					converted_count += 1
			
			if converted_count >= requirement.get("npcsConverted", 2):
				var goal = primary.get("goal", "win")
				return "%s WINS! (%s)" % [player_role.to_upper(), goal]
		return ""
	
	# Check meter requirement - win when meter is maxed out
	if requirement.has("meter"):
		var meter_name = requirement["meter"]
		var meter = player.get_meter(meter_name)
		
		if meter and meter.value >= meter.max_value:
			# For admirer marriage win, also check if love interest is married and unconverted
			if player_role == "admirer" and primary.get("goal") == "marry_love_interest":
				# Find the love interest (should be Katy)
				for npc_id in npcs:
					var npc = npcs[npc_id]
					if npc.is_love_interest and npc.married and not npc.converted:
						var goal = primary.get("goal", "win")
						return "%s WINS! (%s)" % [player_role.to_upper(), goal]
				# Love meter is max but marriage condition not met
				return ""
			
			var goal = primary.get("goal", "win")
			return "%s WINS! (%s)" % [player_role.to_upper(), goal]
	
	return ""

func get_player_goals(player_role: String) -> Dictionary:
	var role_config = config.get("roles", {}).get(player_role, {})
	var goals_config = role_config.get("goals", {})
	var win_config = config.get("gameRules", {}).get("winConditions", {}).get(player_role, {})
	
	return {
		"goals": goals_config,
		"win_condition": win_config
	}

func get_game_status() -> Dictionary:
	var players_dict = {}
	for role in players:
		players_dict[role] = players[role].to_dict()
	
	var npcs_dict = {}
	for npc_id in npcs:
		npcs_dict[npc_id] = npcs[npc_id].to_dict()
	
	return {
		"turn": current_turn,
		"max_turns": max_turns,
		"current_role": get_current_role(),
		"players": players_dict,
		"npcs": npcs_dict,
		"editorial_focus": editorial_focus,
		"admirer_reported": admirer_reported
	}
