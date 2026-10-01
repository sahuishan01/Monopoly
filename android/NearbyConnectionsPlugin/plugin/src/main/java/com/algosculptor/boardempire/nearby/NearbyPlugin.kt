package com.algosculptor.boardempire.nearby

import android.Manifest
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.google.android.gms.nearby.Nearby
import com.google.android.gms.nearby.connection.AdvertisingOptions
import com.google.android.gms.nearby.connection.ConnectionInfo
import com.google.android.gms.nearby.connection.ConnectionLifecycleCallback
import com.google.android.gms.nearby.connection.ConnectionResolution
import com.google.android.gms.nearby.connection.ConnectionsClient
import com.google.android.gms.nearby.connection.DiscoveredEndpointInfo
import com.google.android.gms.nearby.connection.DiscoveryOptions
import com.google.android.gms.nearby.connection.EndpointDiscoveryCallback
import com.google.android.gms.nearby.connection.Payload
import com.google.android.gms.nearby.connection.PayloadCallback
import com.google.android.gms.nearby.connection.PayloadTransferUpdate
import com.google.android.gms.nearby.connection.Strategy
import org.godotengine.godot.Godot
import org.godotengine.godot.plugin.GodotPlugin
import org.godotengine.godot.plugin.SignalInfo
import org.godotengine.godot.plugin.UsedByGodot

/**
 * Thin bridge between Godot and Google Nearby Connections.
 *
 * The plugin knows nothing about the game: it advertises / discovers a service, lets both sides
 * confirm the short authentication code, and moves opaque byte payloads. One device (the host)
 * advertises and every other device connects to it (star topology).
 */
class NearbyPlugin(godot: Godot) : GodotPlugin(godot) {

    private val strategy = Strategy.P2P_STAR
    private val connected = HashSet<String>()

    private val client: ConnectionsClient?
        get() = activity?.let { Nearby.getConnectionsClient(it) }

    override fun getPluginName(): String = "BoardEmpireNearby"

    override fun getPluginSignals(): Set<SignalInfo> = setOf(
        SignalInfo("endpoint_found", String::class.java, String::class.java),
        SignalInfo("endpoint_lost", String::class.java),
        SignalInfo("connection_initiated", String::class.java, String::class.java, String::class.java, java.lang.Boolean::class.java),
        SignalInfo("connection_result", String::class.java, java.lang.Boolean::class.java),
        SignalInfo("disconnected", String::class.java),
        SignalInfo("payload_received", String::class.java, ByteArray::class.java),
        SignalInfo("status", String::class.java),
    )

    private fun requiredPermissions(): Array<String> = when {
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU -> arrayOf(
            Manifest.permission.BLUETOOTH_ADVERTISE,
            Manifest.permission.BLUETOOTH_CONNECT,
            Manifest.permission.BLUETOOTH_SCAN,
            Manifest.permission.NEARBY_WIFI_DEVICES,
        )
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.S -> arrayOf(
            Manifest.permission.BLUETOOTH_ADVERTISE,
            Manifest.permission.BLUETOOTH_CONNECT,
            Manifest.permission.BLUETOOTH_SCAN,
            Manifest.permission.ACCESS_FINE_LOCATION,
        )
        Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q -> arrayOf(Manifest.permission.ACCESS_FINE_LOCATION)
        else -> arrayOf(Manifest.permission.ACCESS_COARSE_LOCATION)
    }

    @UsedByGodot
    fun hasPermissions(): Boolean {
        val context = activity ?: return false
        return requiredPermissions().all {
            ContextCompat.checkSelfPermission(context, it) == PackageManager.PERMISSION_GRANTED
        }
    }

    @UsedByGodot
    fun requestPermissions() {
        val host = activity ?: return
        ActivityCompat.requestPermissions(host, requiredPermissions(), 4711)
    }

    private fun status(message: String) = emitSignal("status", message)

    @UsedByGodot
    fun startAdvertising(name: String, serviceId: String) {
        val options = AdvertisingOptions.Builder().setStrategy(strategy).build()
        client?.startAdvertising(name, serviceId, lifecycle, options)
            ?.addOnSuccessListener { status("Visible to nearby players") }
            ?.addOnFailureListener { status("Could not start advertising: ${it.message}") }
    }

    @UsedByGodot
    fun stopAdvertising() {
        client?.stopAdvertising()
    }

    @UsedByGodot
    fun startDiscovery(serviceId: String) {
        val options = DiscoveryOptions.Builder().setStrategy(strategy).build()
        client?.startDiscovery(serviceId, discovery, options)
            ?.addOnSuccessListener { status("Searching for nearby games…") }
            ?.addOnFailureListener { status("Could not start searching: ${it.message}") }
    }

    @UsedByGodot
    fun stopDiscovery() {
        client?.stopDiscovery()
    }

    @UsedByGodot
    fun requestConnection(localName: String, endpointId: String) {
        client?.requestConnection(localName, endpointId, lifecycle)
            ?.addOnFailureListener {
                status("Connection request failed: ${it.message}")
                emitSignal("connection_result", endpointId, false)
            }
    }

    @UsedByGodot
    fun acceptConnection(endpointId: String) {
        client?.acceptConnection(endpointId, payloads)
    }

    @UsedByGodot
    fun rejectConnection(endpointId: String) {
        client?.rejectConnection(endpointId)
    }

    @UsedByGodot
    fun sendBytes(endpointId: String, data: ByteArray) {
        if (!connected.contains(endpointId)) return
        client?.sendPayload(endpointId, Payload.fromBytes(data))
    }

    @UsedByGodot
    fun disconnect(endpointId: String) {
        client?.disconnectFromEndpoint(endpointId)
        if (connected.remove(endpointId)) emitSignal("disconnected", endpointId)
    }

    @UsedByGodot
    fun stopAll() {
        client?.stopAllEndpoints()
        client?.stopAdvertising()
        client?.stopDiscovery()
        connected.clear()
    }

    override fun onMainDestroy() {
        stopAll()
        super.onMainDestroy()
    }

    private val lifecycle = object : ConnectionLifecycleCallback() {
        override fun onConnectionInitiated(endpointId: String, info: ConnectionInfo) {
            // Both devices display these digits; the players confirm they match before accepting.
            emitSignal("connection_initiated", endpointId, info.endpointName, info.authenticationDigits, info.isIncomingConnection)
        }

        override fun onConnectionResult(endpointId: String, result: ConnectionResolution) {
            val ok = result.status.isSuccess
            if (ok) connected.add(endpointId)
            emitSignal("connection_result", endpointId, ok)
        }

        override fun onDisconnected(endpointId: String) {
            connected.remove(endpointId)
            emitSignal("disconnected", endpointId)
        }
    }

    private val discovery = object : EndpointDiscoveryCallback() {
        override fun onEndpointFound(endpointId: String, info: DiscoveredEndpointInfo) {
            emitSignal("endpoint_found", endpointId, info.endpointName)
        }

        override fun onEndpointLost(endpointId: String) {
            emitSignal("endpoint_lost", endpointId)
        }
    }

    private val payloads = object : PayloadCallback() {
        override fun onPayloadReceived(endpointId: String, payload: Payload) {
            if (payload.type != Payload.Type.BYTES) return
            val bytes = payload.asBytes() ?: return
            emitSignal("payload_received", endpointId, bytes)
        }

        override fun onPayloadTransferUpdate(endpointId: String, update: PayloadTransferUpdate) {
            // BYTES payloads arrive whole; nothing to track.
        }
    }
}
