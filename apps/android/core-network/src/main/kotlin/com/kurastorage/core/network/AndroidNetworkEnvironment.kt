package com.kurastorage.core.network

import android.net.ConnectivityManager
import android.net.Network
import android.net.NetworkCapabilities
import com.kurastorage.core.model.ServerHealth
import com.kurastorage.core.model.StorageAvailability
import okhttp3.OkHttpClient
import java.net.InetAddress
import java.net.Socket
import javax.net.SocketFactory

class AndroidLocalNetworkSource(
    private val connectivityManager: ConnectivityManager,
) : LocalNetworkSource {
    private val networks = mutableMapOf<String, Network>()
    private var lastBaseNetwork: Network? = null

    @Suppress("DEPRECATION", "ReturnCount")
    override fun currentBaseNetwork(): LocalNetworkSnapshot? {
        val network =
            connectivityManager.allNetworks.firstOrNull { candidate ->
                val capabilities = connectivityManager.getNetworkCapabilities(candidate) ?: return@firstOrNull false
                val baseTransport =
                    capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI) ||
                        capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)
                baseTransport && !capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)
            } ?: run {
                lastBaseNetwork = null
                return null
            }
        val properties = connectivityManager.getLinkProperties(network) ?: return null
        val id = network.toString()
        networks[id] = network
        lastBaseNetwork = network
        return LocalNetworkSnapshot(
            networkId = id,
            addresses =
                properties.linkAddresses.map {
                    InterfaceAddress(it.address, it.prefixLength)
                },
        )
    }

    fun network(networkId: String): Network? = networks[networkId]

    fun lastNetwork(): Network? = lastBaseNetwork

    fun refreshingSocketFactory(): SocketFactory =
        RefreshingSocketFactory {
            currentBaseNetwork()
            lastBaseNetwork?.socketFactory
        }
}

internal class RefreshingSocketFactory(
    private val delegateProvider: () -> SocketFactory?,
) : SocketFactory() {
    private fun delegate(): SocketFactory = requireNotNull(delegateProvider()) { "Local network is unavailable" }

    override fun createSocket(): Socket = delegate().createSocket()

    override fun createSocket(
        host: String,
        port: Int,
    ): Socket = delegate().createSocket(host, port)

    override fun createSocket(
        host: String,
        port: Int,
        localHost: InetAddress,
        localPort: Int,
    ): Socket = delegate().createSocket(host, port, localHost, localPort)

    override fun createSocket(
        host: InetAddress,
        port: Int,
    ): Socket = delegate().createSocket(host, port)

    override fun createSocket(
        address: InetAddress,
        port: Int,
        localAddress: InetAddress,
        localPort: Int,
    ): Socket = delegate().createSocket(address, port, localAddress, localPort)
}

class AndroidHealthProbe(
    private val localNetworkSource: AndroidLocalNetworkSource,
    private val baseClient: OkHttpClient,
) : HealthProbe {
    override suspend fun check(target: ProbeTarget): ServerHealth {
        val builder =
            baseClient
                .newBuilder()
                .dns(FixedAddressDns(target.apiHostname, target.address))
        target.bindNetworkId?.let { networkId ->
            val network = requireNotNull(localNetworkSource.network(networkId))
            builder.socketFactory(network.socketFactory)
        }
        val health =
            KuraStorageApi(
                baseUrl = "https://${target.apiHostname}/api/v1",
                client = builder.build(),
            ).health()
        require(health.api == "AVAILABLE")
        return ServerHealth(
            protocolVersion = health.protocolVersion,
            storage =
                if (health.storage == "AVAILABLE") {
                    StorageAvailability.AVAILABLE
                } else {
                    StorageAvailability.UNAVAILABLE
                },
        )
    }
}
