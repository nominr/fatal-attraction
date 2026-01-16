extends Node
class_name NetworkClient

signal assigned_role(role: String)
signal snapshot_received(state: Dictionary)
signal turn_started(data: Dictionary)
signal turn_ended(data: Dictionary)
signal action_result(data: Dictionary)
signal game_over(message: String)

var tcp := StreamPeerTCP.new()
var connected: bool = false
var _buffer := ""
var _role: String = ""
var _player_name: String = "Player"

func connect_to_server(host: String, port: int, player_name: String = "Player") -> void:
    _player_name = player_name
    var err = tcp.connect_to_host(host, port)
    if err != OK:
        push_error("Failed to connect: %s" % err)
        return
    connected = true
    set_process(true)
    # Send join
    _send_json({
        "type": "join",
        "player_name": player_name
    })

func perform_action(npc_id: String, action_id: String) -> void:
    if not connected:
        return
    _send_json({
        "type": "perform_action",
        "npc_id": npc_id,
        "action_id": action_id,
        "role": _role
    })

func _process(_delta: float) -> void:
    if not connected:
        return
    if tcp.get_status() != StreamPeerTCP.STATUS_CONNECTED:
        return
    var available = tcp.get_available_bytes()
    if available > 0:
        var chunk = tcp.get_utf8_string(available)
        _buffer += chunk
        var parts = _buffer.split("\n")
        # Keep last partial
        _buffer = parts.pop_back()
        for line in parts:
            if line.strip_edges() == "":
                continue
            var json := JSON.new()
            var res = json.parse(line)
            if res != OK:
                continue
            var obj: Dictionary = json.data
            _handle_message(obj)

func _handle_message(msg: Dictionary) -> void:
    var t := msg.get("type", "")
    match t:
        "assigned_role":
            _role = String(msg.get("role", ""))
            assigned_role.emit(_role)
        "snapshot":
            var state: Dictionary = msg.get("state", {})
            snapshot_received.emit(state)
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
    tcp.flush()
