extends Control

const FIELD_SIZE = Vector2(1800, 1200)
var forest_seed = 1234
var density = 0.88
var tree_scale = 1.0
var variation = 0.26
var clearing_radius = 145.0
var trees: MultiMeshInstance2D
var tree_material: ShaderMaterial
var ground_material: ShaderMaterial
var viewport: SubViewport
var camera: Camera2D
var stats: Label
var controls = {}
var rebuilding = false
var dragging = false

func _ready():
    _build_scene()
    _regenerate()
    if "--capture" in OS.get_cmdline_user_args():
        for i in range(15): await RenderingServer.frame_post_draw
        DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path("res://.godot"))
        var error = get_viewport().get_texture().get_image().save_png("res://.godot/forest-lab.png")
        print("FOREST LAB capture=", error, " trees=", trees.multimesh.instance_count)
        camera.zoom = Vector2.ONE * 1.65
        camera.position = Vector2(1030, 670)
        for i in range(3): await RenderingServer.frame_post_draw
        get_viewport().get_texture().get_image().save_png("res://.godot/forest-lab-detail.png")
        var dense_count = trees.multimesh.instance_count
        controls["Densidade"].value = 0.40
        assert(trees.multimesh.instance_count < dense_count, "O controle de densidade precisa alterar o lote.")
        controls["Escala das árvores"].value = 0.80
        controls["Variação de altura"].value = 0.45
        controls["Tamanho da clareira"].value = 210
        _palette(1)
        for i in range(3): await RenderingServer.frame_post_draw
        print("FOREST controls passed: density/scale/variation/clearing/palette")
        get_tree().quit(0 if error == OK else 1)

func _build_scene():
    var margin = MarginContainer.new()
    margin.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
    for side in ["left", "right", "top", "bottom"]: margin.add_theme_constant_override("margin_" + side, 22)
    add_child(margin)
    var column = VBoxContainer.new()
    column.add_theme_constant_override("separation", 14)
    margin.add_child(column)
    var title = Label.new()
    title.text = "FOREST LAB   /   estudo de pinhal"
    title.add_theme_font_size_override("font_size", 26)
    title.modulate = Color("d9d3ac")
    column.add_child(title)
    var subtitle = Label.new()
    subtitle.text = "Referência: Battle Brothers  ·  árvores procedurais em shader  ·  arte 2D com sobreposição"
    subtitle.modulate = Color("a3aa96")
    column.add_child(subtitle)
    var row = HBoxContainer.new()
    row.size_flags_vertical = Control.SIZE_EXPAND_FILL
    row.add_theme_constant_override("separation", 22)
    column.add_child(row)
    var container = SubViewportContainer.new()
    container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
    container.size_flags_vertical = Control.SIZE_EXPAND_FILL
    container.stretch = true
    row.add_child(container)
    viewport = SubViewport.new()
    viewport.size = Vector2i(1050, 750)
    viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
    container.add_child(viewport)
    var world = Node2D.new()
    viewport.add_child(world)
    var ground = Polygon2D.new()
    ground.polygon = PackedVector2Array([Vector2.ZERO, Vector2(FIELD_SIZE.x, 0), FIELD_SIZE, Vector2(0, FIELD_SIZE.y)])
    ground.uv = PackedVector2Array([Vector2.ZERO, Vector2.RIGHT, Vector2.ONE, Vector2.DOWN])
    var ground_image = Image.create(1, 1, false, Image.FORMAT_RGBA8)
    ground_image.fill(Color.WHITE)
    ground.texture = ImageTexture.create_from_image(ground_image)
    ground_material = ShaderMaterial.new()
    ground_material.shader = load("res://ground.gdshader")
    ground.material = ground_material
    world.add_child(ground)
    trees = MultiMeshInstance2D.new()
    tree_material = ShaderMaterial.new()
    tree_material.shader = load("res://forest.gdshader")
    trees.material = tree_material
    world.add_child(trees)
    camera = Camera2D.new()
    camera.position = FIELD_SIZE * 0.5
    camera.zoom = Vector2.ONE * 0.87
    world.add_child(camera)
    container.gui_input.connect(_canvas_input)
    var panel = VBoxContainer.new()
    panel.custom_minimum_size.x = 275
    panel.add_theme_constant_override("separation", 9)
    row.add_child(panel)
    var label = Label.new()
    label.text = "COMPOSIÇÃO"
    label.modulate = Color("d9d3ac")
    panel.add_child(label)
    _slider(panel, "Densidade", 0.15, 1.0, density, func(v): density = v; _regenerate())
    _slider(panel, "Escala das árvores", 0.65, 1.45, tree_scale, func(v): tree_scale = v; _regenerate())
    _slider(panel, "Variação de altura", 0.0, 0.50, variation, func(v): variation = v; _regenerate())
    _slider(panel, "Tamanho da clareira", 0, 260, clearing_radius, func(v): clearing_radius = v; _regenerate())
    _slider(panel, "Luz lateral", 0.0, 1.0, 0.65, func(v): tree_material.set_shader_parameter("light_strength", v))
    _slider(panel, "Contorno das árvores", 0.0, 1.0, 0.65, func(v): tree_material.set_shader_parameter("ink_strength", v))
    var shadows = CheckButton.new()
    shadows.text = "Sombras no solo"
    shadows.button_pressed = true
    shadows.toggled.connect(func(value): tree_material.set_shader_parameter("show_shadows", value))
    panel.add_child(shadows)
    var grid = CheckButton.new()
    grid.text = "Mostrar grade de referência"
    grid.toggled.connect(func(value): ground_material.set_shader_parameter("show_grid", value))
    panel.add_child(grid)
    var palette = OptionButton.new()
    palette.add_item("Verde escuro / pinhal")
    palette.add_item("Verde oliva / luz quente")
    palette.add_item("Frio / montanha")
    palette.item_selected.connect(_palette)
    panel.add_child(palette)
    var reroll = Button.new()
    reroll.text = "Nova distribuição"
    reroll.pressed.connect(func(): forest_seed += 1; _regenerate())
    panel.add_child(reroll)
    var reset = Button.new()
    reset.text = "Centralizar câmera"
    reset.pressed.connect(func(): camera.position = FIELD_SIZE * 0.5; camera.zoom = Vector2.ONE * 0.87)
    panel.add_child(reset)
    stats = Label.new()
    stats.modulate = Color("a3aa96")
    panel.add_child(stats)
    var help = Label.new()
    help.text = "Roda: aproximar / afastar\nBotão direito: mover\n\nEstudo procedural inicial."
    help.modulate = Color("8c9785")
    panel.add_child(help)
    _palette(0)

func _slider(parent, caption, minimum, maximum, initial, callback):
    var label = Label.new()
    label.text = caption
    parent.add_child(label)
    var slider = HSlider.new()
    slider.min_value = minimum
    slider.max_value = maximum
    slider.step = (maximum - minimum) / 100.0
    slider.value = initial
    slider.value_changed.connect(callback)
    parent.add_child(slider)
    controls[caption] = slider

func _regenerate():
    if rebuilding or not is_instance_valid(trees): return
    rebuilding = true
    var rng = RandomNumberGenerator.new()
    rng.seed = forest_seed
    var noise = FastNoiseLite.new()
    noise.seed = forest_seed
    noise.frequency = 0.0035
    noise.fractal_octaves = 3
    var instances = []
    for y in range(50, 1170, 18):
        for x in range(40, 1770, 23):
            var position = Vector2(x + rng.randf_range(-10, 10), y + rng.randf_range(-7, 7))
            var offset = (position - FIELD_SIZE * 0.5) / Vector2(810, 510)
            var coverage = 0.69 + noise.get_noise_2dv(position) * 0.90 - maxf(0.0, offset.length() - 0.60) * 0.9
            var clearing = position.distance_to(Vector2(1010, 650)) - clearing_radius + noise.get_noise_2dv(position * 3.0) * 65
            var probability = clampf((coverage - 0.30) * 4.0, 0, 1) * density
            probability *= smoothstep(-30, 45, clearing)
            if rng.randf() > probability: continue
            var scale = tree_scale * rng.randf_range(1.0 - variation, 1.0 + variation)
            instances.append([position, scale, Color(rng.randf(), rng.randf(), rng.randf(), rng.randf())])
    instances.sort_custom(func(a, b): return a[0].y < b[0].y)
    var mesh = QuadMesh.new()
    mesh.size = Vector2(90, 96)
    var batch = MultiMesh.new()
    batch.transform_format = MultiMesh.TRANSFORM_2D
    batch.use_custom_data = true
    batch.mesh = mesh
    batch.instance_count = instances.size()
    for i in instances.size():
        var entry = instances[i]
        var scale = entry[1]
        var position = entry[0] - Vector2(0, 43 * scale)
        batch.set_instance_transform_2d(i, Transform2D(0, Vector2.ONE * scale, 0, position))
        batch.set_instance_custom_data(i, entry[2])
    trees.multimesh = batch
    stats.text = "%s árvores · 1 lote\nSeed %s" % [instances.size(), forest_seed]
    print("FOREST rebuilt trees=", instances.size(), " seed=", forest_seed)
    rebuilding = false

func _palette(index):
    var colors = [[Color("111e12"), Color("607044")], [Color("252b15"), Color("8a8c4d")], [Color("172a26"), Color("668579")]][index]
    tree_material.set_shader_parameter("foliage_dark", colors[0])
    tree_material.set_shader_parameter("foliage_light", colors[1])

func _canvas_input(event):
    if event is InputEventMouseButton:
        if event.button_index == MOUSE_BUTTON_RIGHT: dragging = event.pressed
        if event.pressed and event.button_index in [MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_DOWN]:
            var factor = 1.12 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 1.0 / 1.12
            camera.zoom = Vector2.ONE * clampf(camera.zoom.x * factor, 0.45, 3.0)
    elif event is InputEventMouseMotion and dragging:
        camera.position -= event.relative / camera.zoom
