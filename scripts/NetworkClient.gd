extends Node
class_name NetworkClient

signal assigned_role(role: String)
signal snapshot_received(state: Dictionary)
signal state_update(state: Dictionary) # New real-time update signal
signal turn_started(data: Dictionary)
signal turn_ended(data: Dictionary)
signal action_result(data: Dictionary)
signal game_over(message: String)

var tcp := StreamPeerTCP.new()
# ... (existing vars)

# ... (inside _handle_message match)
		"state_update":
			var state: Dictionary = msg.get("state", {})
			state_update.emit(state)
		"turn_started":
			turn_started.emit(msg)
		"turn_ended":
			turn_ended.emit(msg)
		"action_result":
			action_result.emit(msg)
		"game_over":
			var m = String(msg.get("message", ""))
			game_over.emit(m)
		_:
			pass

func _send_json(d: Dictionary) -> void:
	var payload := JSON.stringify(d) + "\n"
	var data := payload.to_utf8_buffer()
	tcp.put_data(data)
