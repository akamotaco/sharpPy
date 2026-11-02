@tool
extends EditorExportPlugin
## Builds the bundle when a game is exported and puts it in the pack. The game then calls
## PyHost.UseBundle with the same path. Sources stay out unless exclude_sources is off.


func _get_name() -> String:
	return "SharpPyBundle"


func _export_begin(_features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
	var scripts: PackedStringArray = ProjectSettings.get_setting("sharppy/bundle/scripts")
	if scripts.is_empty():
		push_warning("SharpPy: sharppy/bundle/scripts is empty, no bundle was made")
		return
	var built := ProjectSettings.globalize_path("user://sharppy_export.spk")
	var runner: PackedStringArray = ProjectSettings.get_setting("sharppy/bundle/runner")
	var arguments := runner.slice(1)
	arguments.append(ProjectSettings.globalize_path(ProjectSettings.get_setting("sharppy/bundle/tool")))
	arguments.append_array(["--output", built])
	arguments.append_array(["--path", ProjectSettings.globalize_path(ProjectSettings.get_setting("sharppy/bundle/library"))])
	for script in scripts:
		arguments.append(ProjectSettings.globalize_path(script))
	var output := []
	var status := OS.execute(runner[0], arguments, output, true)
	if status != 0:
		push_error("SharpPy: bundle tool failed: " + "\n".join(output))
		return
	print("SharpPy: ", output[0].strip_edges().get_slice("\n", 0))
	add_file(ProjectSettings.get_setting("sharppy/bundle/output"), FileAccess.get_file_as_bytes(built), false)


func _export_file(path: String, _type: String, _features: PackedStringArray) -> void:
	if ProjectSettings.get_setting("sharppy/bundle/exclude_sources") and path.ends_with(".py"):
		skip()
