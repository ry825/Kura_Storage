package com.kurastorage.core.data.media

import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.media.NetworkQualityContext

enum class NetworkTransport {
    WIFI,
    CELLULAR,
    UNKNOWN,
}

fun interface NetworkTransportSource {
    fun activeTransport(): NetworkTransport
}

fun interface RegisteredWifiSource {
    suspend fun isRegistered(): Boolean
}

object NoRegisteredWifiSource : RegisteredWifiSource {
    override suspend fun isRegistered() = false
}

class AndroidNetworkTransportSource(
    private val connectivityManager: ConnectivityManager,
) : NetworkTransportSource {
    override fun activeTransport(): NetworkTransport {
        val capabilities =
            connectivityManager.activeNetwork?.let(connectivityManager::getNetworkCapabilities)
        return when {
            capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) == true -> NetworkTransport.CELLULAR
            capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true -> NetworkTransport.WIFI
            else -> NetworkTransport.UNKNOWN
        }
    }
}

class NetworkQualityContextResolver(
    private val transportSource: NetworkTransportSource,
    private val registeredWifiSource: RegisteredWifiSource = NoRegisteredWifiSource,
) {
    suspend fun resolve(route: ConnectionRoute): NetworkQualityContext =
        when (route) {
            ConnectionRoute.LOCAL_DIRECT -> NetworkQualityContext.LOCAL_DIRECT
            ConnectionRoute.REMOTE_SECURE ->
                when (transportSource.activeTransport()) {
                    NetworkTransport.CELLULAR -> NetworkQualityContext.REMOTE_MOBILE
                    NetworkTransport.WIFI ->
                        if (registeredWifiSource.isRegistered()) {
                            NetworkQualityContext.REGISTERED_REMOTE_WIFI
                        } else {
                            NetworkQualityContext.UNREGISTERED_REMOTE_WIFI
                        }
                    NetworkTransport.UNKNOWN -> NetworkQualityContext.UNREGISTERED_REMOTE_WIFI
                }
        }
}
