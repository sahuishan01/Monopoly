# BoardEmpire Nearby plugin

Godot 4 Android plugin (v2) that exposes Google Nearby Connections to the game as the
`BoardEmpireNearby` singleton. The C# side is `client/src/Net/NearbyTransport.cs`.

```bash
# needs JDK 17 and the Android SDK (ANDROID_HOME); run on x86_64 or in CI
gradle wrapper --gradle-version 8.10.2   # once
./gradlew :plugin:deployToGodot
```

`deployToGodot` builds the debug and release AARs and copies them to
`client/addons/boardempire_nearby/bin/`. The Godot export plugin in that addon picks them up
and adds the Play Services dependency when the Android preset uses a Gradle build. Without the
AARs the game still exports and simply hides the Nearby options.
