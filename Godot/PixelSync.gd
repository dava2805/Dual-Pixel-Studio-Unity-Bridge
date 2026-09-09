@tool
extends Sprite2D
class_name DualPixelLink

# ---------------------------------------------------------
# PixelSync - Godot Sync Link
# ---------------------------------------------------------
# Attach this script to a Sprite2D node.
# It will automatically discover the PixelSync app 
# on your local Wi-Fi and live-sync your pixel art!
# ---------------------------------------------------------

@export var manual_ip_address: String = ""
@export var manual_port: int = 8642
@export var auto_discover: bool = true
@export var refresh_rate_hz: float = 8.0

var _http_request: HTTPRequest
var _udp_peer: PacketPeerUDP
var _is_fetching: bool = false
var _discovered_ip: String = ""
var _discovered_port: int = 0
var _timer: Timer

func _ready():
	# Create HTTP Request node dynamically
	_http_request = HTTPRequest.new()
	add_child(_http_request)
	_http_request.request_completed.connect(_on_image_downloaded)
	
	if auto_discover:
		_setup_udp()
		
	# Setup polling timer
	_timer = Timer.new()
	add_child(_timer)
	_timer.wait_time = 1.0 / refresh_rate_hz
	_timer.timeout.connect(_poll_app)
	_timer.start()

func _setup_udp():
	_udp_peer = PacketPeerUDP.new()
	var err = _udp_peer.bind(8644)
	if err == OK:
		print("PixelSync: Listening for auto-discovery...")
	else:
		print("PixelSync: Failed to bind UDP port 8644. Auto-discovery may not work.")

func _process(_delta):
	if auto_discover and _udp_peer != null and _udp_peer.get_available_packet_count() > 0:
		var packet = _udp_peer.get_packet().get_string_from_utf8()
		# Expected format: PIXELSYNC_UNITY:IP:PORT:ProjectName
		if packet.begins_with("PIXELSYNC_UNITY:"):
			var parts = packet.split(":")
			if parts.size() >= 3:
				var new_ip = parts[1]
				var new_port = parts[2].to_int()
				if _discovered_ip != new_ip or _discovered_port != new_port:
					_discovered_ip = new_ip
					_discovered_port = new_port
					print("PixelSync Discovered: ", _discovered_ip, ":", _discovered_port)

func _poll_app():
	if _is_fetching:
		return
		
	var target_ip = _discovered_ip if (auto_discover and _discovered_ip != "") else manual_ip_address
	var target_port = _discovered_port if (auto_discover and _discovered_port != 0) else manual_port
	
	if target_ip == "":
		return
		
	var url = "http://%s:%d/api/sprite.png" % [target_ip, target_port]
	
	_is_fetching = true
	var err = _http_request.request(url)
	if err != OK:
		_is_fetching = false

func _on_image_downloaded(result: int, response_code: int, headers: PackedStringArray, body: PackedByteArray):
	_is_fetching = false
	if result == HTTPRequest.RESULT_SUCCESS and response_code == 200:
		var img = Image.new()
		var err = img.load_png_from_buffer(body)
		if err == OK:
			var tex = ImageTexture.create_from_image(img)
			self.texture = tex
			
			# Fix blurry pixels in Godot 4 (Nearest Neighbor filtering)
			self.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
