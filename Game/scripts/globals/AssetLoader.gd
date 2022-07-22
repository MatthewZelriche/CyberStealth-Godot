extends Node

# Called when the node enters the scene tree for the first time.
func _ready():
	load_user_assets()

func load_user_assets():
	# Don't execute in editor mode, this is meant for standalone only.
	# All "internal" textures will be imported normally.
	#if !OS.has_feature("standalone"):
	#	return
	
	var exePath = OS.get_executable_path().get_base_dir() + "/"
	var tabletop_importer = ClassDB.instance("TabletopImporter")
	var packer = PCKPacker.new()
	
	# create cache folder for when we pack
	var dir = Directory.new()
	dir.open(exePath)
	dir.make_dir(".cache")
	packer.pck_start("user_tex.pck")

		
	# Get user texture directory
	var path = exePath
	print("Globalized path: " + path)
	path = path + "trenchbroom_data/textures/user/"
	print("Globalized with user dir: " + path)
	dir = Directory.new()
	if dir.open(path) != 0:
		print("ERR: Could not open user texture path")
		return
		
	# Iterate over user textures to generate .imports
	dir.list_dir_begin()
	while true:
		var file = dir.get_next()
		if file == "":
			break
		
		print("Filepath: " + path + file)
		if ".png" in file && !".import" in file:
			if tabletop_importer.import(path + file) != 0:
				print("ERR: Could not import user texture file")
				continue
				
	
	dir.list_dir_begin()
	while true:
		var file = dir.get_next()
		if file == "":
			break
		
		if ".png" in file:
			#tabletop_importer.copy_file(path + file, OS.get_executable_path().get_base_dir() + "/.cache/" + file, true)
			packer.add_file("res://trenchbroom_data/textures/user/" + file, path + file)
			
	dir.list_dir_end()
	packer.flush()
	
	var success = ProjectSettings.load_resource_pack(exePath + "user_tex.pck")
	print(exePath + "user_tex.pck")
	if success:
		print("BUB")
	else:
		print("Failed to load user_tex.pck")
		
	print(load("res://trenchbroom_data/textures/user/CustomMat.png"))
