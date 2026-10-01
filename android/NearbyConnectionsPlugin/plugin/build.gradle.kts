plugins {
    id("com.android.library")
    id("org.jetbrains.kotlin.android")
}

val pluginName = "BoardEmpireNearby"
val godotVersion = "4.7.2.stable"

android {
    namespace = "com.algosculptor.boardempire.nearby"
    compileSdk = 35

    defaultConfig {
        minSdk = 24
        manifestPlaceholders["godotPluginName"] = pluginName
        manifestPlaceholders["godotPluginClass"] = "com.algosculptor.boardempire.nearby.NearbyPlugin"
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    // Provided by the Godot Android template at runtime.
    compileOnly("org.godotengine:godot:$godotVersion")
    // Must match the dependency declared in client/addons/boardempire_nearby/export_plugin.gd.
    implementation("com.google.android.gms:play-services-nearby:19.3.0")
}

// Copies the built archives to where the Godot export plugin looks for them.
val copyDebug by tasks.registering(Copy::class) {
    dependsOn("assembleDebug")
    from(layout.buildDirectory.dir("outputs/aar")) { include("plugin-debug.aar") }
    into(rootProject.layout.projectDirectory.dir("../../client/addons/boardempire_nearby/bin"))
    rename { "$pluginName-debug.aar" }
}
val copyRelease by tasks.registering(Copy::class) {
    dependsOn("assembleRelease")
    from(layout.buildDirectory.dir("outputs/aar")) { include("plugin-release.aar") }
    into(rootProject.layout.projectDirectory.dir("../../client/addons/boardempire_nearby/bin"))
    rename { "$pluginName-release.aar" }
}
tasks.register("deployToGodot") {
    dependsOn(copyDebug, copyRelease)
}
