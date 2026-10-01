@tool
extends EditorPlugin

# Registers the Nearby Connections Android library with the exporter. The archives are built
# from android/NearbyConnectionsPlugin (`./gradlew :plugin:deployToGodot`). When they are
# missing the export still succeeds; the game then hides its Nearby options.

var _export_plugin: AndroidExportPlugin


func _enter_tree() -> void:
	_export_plugin = AndroidExportPlugin.new()
	add_export_plugin(_export_plugin)


func _exit_tree() -> void:
	remove_export_plugin(_export_plugin)
	_export_plugin = null


class AndroidExportPlugin:
	extends EditorExportPlugin

	const PLUGIN_NAME := "BoardEmpireNearby"
	const BIN := "res://addons/boardempire_nearby/bin/"

	func _supports_platform(platform: EditorExportPlatform) -> bool:
		return platform is EditorExportPlatformAndroid

	func _get_android_libraries(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		var file := PLUGIN_NAME + ("-debug.aar" if debug else "-release.aar")
		if not FileAccess.file_exists(BIN + file):
			return PackedStringArray()
		return PackedStringArray(["boardempire_nearby/bin/" + file])

	func _get_android_dependencies(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
		if _get_android_libraries(platform, debug).is_empty():
			return PackedStringArray()
		return PackedStringArray(["com.google.android.gms:play-services-nearby:19.3.0"])

	func _get_name() -> String:
		return PLUGIN_NAME
