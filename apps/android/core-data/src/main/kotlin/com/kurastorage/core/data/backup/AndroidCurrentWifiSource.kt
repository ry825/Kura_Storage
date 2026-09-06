package com.kurastorage.core.data.backup

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.location.LocationManager
import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import android.net.wifi.WifiInfo
import android.net.wifi.WifiManager
import android.os.Build

object WifiPermissionPolicy {
    fun requiredPermissions(sdkInt: Int): Set<String> =
        if (sdkInt >= Build.VERSION_CODES.TIRAMISU) {
            setOf(
                Manifest.permission.NEARBY_WIFI_DEVICES,
                Manifest.permission.ACCESS_COARSE_LOCATION,
                Manifest.permission.ACCESS_FINE_LOCATION,
            )
        } else if (sdkInt >= Build.VERSION_CODES.S) {
            setOf(
                Manifest.permission.ACCESS_COARSE_LOCATION,
                Manifest.permission.ACCESS_FINE_LOCATION,
            )
        } else {
            setOf(Manifest.permission.ACCESS_FINE_LOCATION)
        }

    fun missingResult(
        permissions: Set<String>,
        canRequestAgain: (String) -> Boolean,
    ): CurrentWifiResult =
        if (permissions.none(canRequestAgain)) {
            CurrentWifiResult.PermissionPermanentlyDenied
        } else {
            CurrentWifiResult.PermissionRequired(permissions)
        }
}

object WifiDetectionPolicy {
    fun blockedResult(
        sdkInt: Int,
        grantedPermissions: Set<String>,
        locationServicesEnabled: Boolean,
        canRequestPermissionAgain: (String) -> Boolean = { true },
    ): CurrentWifiResult? {
        val missing = WifiPermissionPolicy.requiredPermissions(sdkInt) - grantedPermissions
        return when {
            missing.isNotEmpty() -> WifiPermissionPolicy.missingResult(missing, canRequestPermissionAgain)
            !locationServicesEnabled -> CurrentWifiResult.LocationServicesDisabled
            else -> null
        }
    }
}

data class WifiIdentityCandidate(
    val ssid: String?,
    val bssid: String?,
)

object WifiIdentitySelector {
    fun select(
        candidates: List<WifiIdentityCandidate>,
        systemMetered: Boolean,
    ): ConnectedWifi? =
        candidates.firstNotNullOfOrNull { candidate ->
            runCatching {
                ConnectedWifi(
                    ssid = WifiIdentifierNormalizer.normalizeSsid(candidate.ssid.orEmpty()),
                    bssid = runCatching { WifiIdentifierNormalizer.normalizeBssid(candidate.bssid) }.getOrNull(),
                    systemMetered = systemMetered,
                )
            }.getOrNull()
        }
}

class AndroidCurrentWifiSource(
    private val context: Context,
    private val connectivityManager: ConnectivityManager,
    private val wifiManager: WifiManager,
    private val locationManager: LocationManager,
    private val sdkInt: Int = Build.VERSION.SDK_INT,
    private val canRequestPermissionAgain: (String) -> Boolean = { true },
) : CurrentWifiSource {
    @Suppress("DEPRECATION", "ReturnCount")
    override fun read(): CurrentWifiResult {
        val requiredPermissions = WifiPermissionPolicy.requiredPermissions(sdkInt)
        val grantedPermissions =
            requiredPermissions.filterTo(mutableSetOf()) { permission ->
                context.checkSelfPermission(permission) == PackageManager.PERMISSION_GRANTED
            }
        WifiDetectionPolicy
            .blockedResult(
                sdkInt = sdkInt,
                grantedPermissions = grantedPermissions,
                locationServicesEnabled = locationManager.isLocationEnabled,
                canRequestPermissionAgain = canRequestPermissionAgain,
            )?.let { return it }

        // A VPN is commonly the active network while ZeroTier is connected. Read the
        // non-VPN Wi-Fi underneath it so Android 13 can still evaluate an allowlisted SSID/BSSID.
        val activeNetwork =
            connectivityManager.allNetworks.firstOrNull { network ->
                connectivityManager.getNetworkCapabilities(network)?.let { capabilities ->
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) &&
                        !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
                } == true
            } ?: return CurrentWifiResult.NotConnected
        val capabilities =
            connectivityManager.getNetworkCapabilities(activeNetwork)
                ?: return CurrentWifiResult.Unavailable
        if (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
            return CurrentWifiResult.NotConnected
        }
        return try {
            val capabilityWifiInfo = capabilities.transportInfo as? WifiInfo
            val connectionWifiInfo = wifiManager.connectionInfo
            val connectedWifi =
                WifiIdentitySelector.select(
                    candidates =
                        listOfNotNull(
                            capabilityWifiInfo?.toCandidate(),
                            connectionWifiInfo?.toCandidate(),
                        ),
                    systemMetered = connectivityManager.isActiveNetworkMetered,
                ) ?: return CurrentWifiResult.Unavailable
            CurrentWifiResult.Available(connectedWifi)
        } catch (_: SecurityException) {
            CurrentWifiResult.PermissionRequired(WifiPermissionPolicy.requiredPermissions(sdkInt))
        }
    }

    private fun WifiInfo.toCandidate() = WifiIdentityCandidate(ssid = ssid, bssid = bssid)
}
