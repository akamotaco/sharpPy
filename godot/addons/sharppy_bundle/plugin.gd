@tool
extends EditorPlugin

var settings := {
	"sharppy/bundle/scripts": PackedStringArray(),
	"sharppy/bundle/library": "res://Lib",
	"sharppy/bundle/output": "res://python/game.spk",
	"sharppy/bundle/runner": PackedStringArray(["python"]),
	"sharppy/bundle/tool": "res://addons/sharppy_bundle/make_bundle.py",
	"sharppy/bundle/exclude_sources": true,
}

var exporter: EditorExportPlugin


func _enter_tree() -> void:
	for key in settings:
		if not ProjectSettings.has_setting(key):
			ProjectSettings.set_setting(key, settings[key])
		ProjectSettings.set_initial_value(key, settings[key])
	exporter = preload("export_plugin.gd").new()
	add_export_plugin(exporter)


func _exit_tree() -> void:
	remove_export_plugin(exporter)
	exporter = null
