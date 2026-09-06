package com.kurastorage.core.data.media

import android.net.ConnectivityManager
import android.net.NetworkCapabilities
import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.media.NetworkQualityContext
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.callbackFlow
import kotlinx.coroutines.flow.distinctUntilChanged
import kotlinx.coroutines.flow.flowOf

enum class NetworkTransport {
    WIFI,
    ETHERNET,
    CELLULAR,
    OTHER_OR_UNKNOWN,
}

fun interface NetworkTransportSource {
    fun activeTransport(): NetworkTransport

    fun observe(): Flow<NetworkTransport> = flowOf(activeTransport())
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
        return NetworkTransportClassifier.classify(capabilities)
    }

    override fun observe(): Flow<NetworkTransport> =
        callbackFlow {
            fun publish() {
                trySend(activeTransport())
            }

            val callback =
                object : ConnectivityManager.NetworkCallback() {
                    override fun onAvailable(network: android.net.Network) = publish()

                    override fun onLost(network: android.net.Network) = publish()

                    override fun onCapabilitiesChanged(
                        network: android.net.Network,
                        networkCapabilities: NetworkCapabilities,
                    ) = publish()
                }
            publish()
            connectivityManager.registerDefaultNetworkCallback(callback)
            awaitClose { connectivityManager.unregisterNetworkCallback(callback) }
        }.distinctUntilChanged()
}

object NetworkTransportClassifier {
    fun classify(capabilities: NetworkCapabilities?): NetworkTransport =
        when {
            capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) == true -> NetworkTransport.WIFI
            capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET) == true -> NetworkTransport.ETHERNET
            capabilities?.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR) == true -> NetworkTransport.CELLULAR
            else -> NetworkTransport.OTHER_OR_UNKNOWN
        }
}

class NetworkQualityContextResolver(
    private val transportSource: NetworkTransportSource,
    private val registeredWifiSource: RegisteredWifiSource = NoRegisteredWifiSource,
) {
    fun activeTransport(): NetworkTransport = transportSource.activeTransport()

    fun observeTransport(): Flow<NetworkTransport> = transportSource.observe()

    suspend fun resolve(
        route: ConnectionRoute,
        transport: NetworkTransport = activeTransport(),
    ): NetworkQualityContext =
        when (route) {
            ConnectionRoute.LOCAL_DIRECT -> NetworkQualityContext.LOCAL_DIRECT
            ConnectionRoute.REMOTE_SECURE ->
                when (transport) {
                    NetworkTransport.CELLULAR -> NetworkQualityContext.REMOTE_MOBILE
                    NetworkTransport.WIFI ->
                        if (registeredWifiSource.isRegistered()) {
                            NetworkQualityContext.REGISTERED_REMOTE_WIFI
                        } else {
                            NetworkQualityContext.UNREGISTERED_REMOTE_WIFI
                        }
                    NetworkTransport.ETHERNET,
                    NetworkTransport.OTHER_OR_UNKNOWN,
                    -> NetworkQualityContext.UNREGISTERED_REMOTE_WIFI
                }
        }
}
