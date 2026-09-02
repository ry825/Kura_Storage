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

class AndroidCurrentWifiSource(
    private val context: Context,
    private val connectivityManager: ConnectivityManager,
    private val wifiManager: WifiManager,
    private val locationManager: LocationManager,
    private val sdkInt: Int = Build.VERSION.SDK_INT,
    private val canRequestPermissionAgain: (String) -> Boolean = { true },
) : CurrentWifiSource {
    @Suppress("ReturnCount")
    override fun read(): CurrentWifiResult {
        val missing =
            WifiPermissionPolicy.requiredPermissions(sdkInt).filterTo(mutableSetOf()) { permission ->
                context.checkSelfPermission(permission) != PackageManager.PERMISSION_GRANTED
            }
        if (missing.isNotEmpty()) return WifiPermissionPolicy.missingResult(missing, canRequestPermissionAgain)
        if (!locationManager.isLocationEnabled) return CurrentWifiResult.LocationServicesDisabled

        val activeNetwork = connectivityManager.activeNetwork ?: return CurrentWifiResult.NotConnectedToWifi
        val capabilities =
            connectivityManager.getNetworkCapabilities(activeNetwork)
                ?: return CurrentWifiResult.InformationUnavailable
        if (!capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) {
            return CurrentWifiResult.NotConnectedToWifi
        }
        return try {
            val wifiInfo =
                if (sdkInt >= Build.VERSION_CODES.S) {
                    capabilities.transportInfo as? WifiInfo
                } else {
                    @Suppress("DEPRECATION")
                    wifiManager.connectionInfo
                } ?: return CurrentWifiResult.InformationUnavailable
            val ssid = WifiIdentifierNormalizer.normalizeSsid(wifiInfo.ssid)
            val bssid = runCatching { WifiIdentifierNormalizer.normalizeBssid(wifiInfo.bssid) }.getOrNull()
            CurrentWifiResult.Connected(
                ConnectedWifi(
                    ssid = ssid,
                    bssid = bssid,
                    systemMetered = connectivityManager.isActiveNetworkMetered,
                ),
            )
        } catch (_: SecurityException) {
            CurrentWifiResult.PermissionRequired(WifiPermissionPolicy.requiredPermissions(sdkInt))
        } catch (_: IllegalArgumentException) {
            CurrentWifiResult.InformationUnavailable
        }
    }
}
