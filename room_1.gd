extends Area2D
@export var room_name: String

signal room_clicked(room_name)

# Called when the node enters the scene tree for the first time.
var hover_poly: Polygon2D
var is_active: bool = false
var is_hovered: bool = false

func _ready() -> void:
	print("Room Ready: ", room_name)
	# Find the CollisionPolygon2D to copy its shape
	for child in get_children():
		if child is CollisionPolygon2D:
			hover_poly = Polygon2D.new()
			hover_poly.polygon = child.polygon
			hover_poly.position = child.position
			hover_poly.color = Color(0, 0, 0, 0) # Transparent start
			add_child(hover_poly)
			print("Created poly for ", room_name)
			break
	
	# Ensure pickable is true
	input_pickable = true
	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	
	update_visual()

func set_active(val: bool):
	if is_active != val:
		print("Room ", room_name, " set_active: ", val)
		is_active = val
		update_visual()

func _on_mouse_entered():
	print("Mouse Enter: ", room_name)
	is_hovered = true
	update_visual()

func _on_mouse_exited():
	is_hovered = false
	update_visual()

func update_visual():
	if hover_poly:
		if is_active:
			# Active: Darker grey
			hover_poly.color = Color(0, 0, 0, 0.6)
		elif is_hovered:
			# Hover: Light grey
			hover_poly.color = Color(0, 0, 0, 0.3) 
		else:
			# Default: Transparent
			hover_poly.color = Color(0, 0, 0, 0)

func _on_room_input_event(_viewport, event, _shape_idx):
	if event is InputEventMouseButton and event.pressed:
		print("CLICKED:", room_name)
		# Emit click and let authoritative game state (server) toggle active cameras.
		# This prevents local UI from desynchronizing and greying more than two rooms.
		room_clicked.emit(room_name)
