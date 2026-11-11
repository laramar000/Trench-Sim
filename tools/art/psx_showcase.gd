extends SceneTree
# Renders figures side by side with and without the PSX shader (client/shaders/psx_figure.gdshader), for a close look.
# godot --rendering-driver opengl3 --path client --script ../tools/art/psx_showcase.gd -- <out dir> <name> <name> ...
# (names are files in client/assets/units without .glb, e.g. trench-pilgrim combat-engineer)

func _initialize() -> void:
	var args := OS.get_cmdline_user_args()
	var out := args[0]
	var names := args.slice(1)
	var shader: Shader = load("res://shaders/psx_figure.gdshader")
	root.size = Vector2i(1280, 720)
	for use_psx in [true, false]:
		var world := Node3D.new()
		root.add_child(world)
		var env := WorldEnvironment.new()
		env.environment = Environment.new()
		env.environment.background_mode = Environment.BG_COLOR
		env.environment.background_color = Color(0.25, 0.27, 0.3)
		env.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
		env.environment.ambient_light_color = Color(0.55, 0.55, 0.6)
		world.add_child(env)
		var sun := DirectionalLight3D.new()
		sun.rotation_degrees = Vector3(-45, -30, 0)
		world.add_child(sun)
		var x := -(names.size() - 1) * 0.45
		for n in names:
			var doc := GLTFDocument.new()
			var state := GLTFState.new()
			doc.append_from_file(ProjectSettings.globalize_path("res://assets/units/%s.glb" % n), state)
			var fig: Node3D = doc.generate_scene(state)
			for mesh in fig.find_children("*", "MeshInstance3D", true, false):
				for i in mesh.mesh.get_surface_count():
					var m = mesh.mesh.surface_get_material(i)
					if m is BaseMaterial3D:
						m.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
						if use_psx and m.albedo_texture:
							var sm := ShaderMaterial.new()
							sm.shader = shader
							sm.set_shader_parameter("albedo_tex", m.albedo_texture)
							sm.set_shader_parameter("albedo_colour", m.albedo_color)
							mesh.set_surface_override_material(i, sm)
			# The client paints parts named "Accent*" flat in the seat colour (Visuals.PaintAccent); do the same.
			var seat := StandardMaterial3D.new()
			seat.albedo_color = Color(0.22, 0.28, 0.4)
			seat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
			for mesh in fig.find_children("Accent*", "MeshInstance3D", true, false):
				mesh.material_override = seat
			fig.position = Vector3(x, 0, 0)
			world.add_child(fig)
			x += 0.9
		var cam := Camera3D.new()
		cam.fov = 30
		world.add_child(cam)
		cam.make_current()
		var cz := float(OS.get_environment("PSX_CAM_Z")) if OS.get_environment("PSX_CAM_Z") != "" else 3.2   # PSX_CAM_Z=1.2 for a close-up
		cam.look_at_from_position(Vector3(0, 0.75 if cz > 2.0 else 0.8, cz), Vector3(0, 0.6 if cz > 2.0 else 0.8, 0))
		for i in 6:
			await process_frame
		root.get_texture().get_image().save_png("%s/psx-showcase-%s.png" % [out, "on" if use_psx else "off"])
		world.queue_free()
		await process_frame
	quit()
