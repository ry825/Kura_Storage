package com.kurastorage.core.network

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.ServerHealth
import java.net.InetAddress
import javax.net.ssl.SSLException

data class LocalNetworkSnapshot(
    val networkId: String,
    val addresses: List<InterfaceAddress>,
)

data class InterfaceAddress(
    val address: InetAddress,
    val prefixLength: Int,
)

data class ProbeTarget(
    val apiHostname: String,
    val address: String,
    val bindNetworkId: String?,
)

fun interface HealthProbe {
    suspend fun check(target: ProbeTarget): ServerHealth
}

fun interface LocalNetworkSource {
    fun currentBaseNetwork(): LocalNetworkSnapshot?
}

class ConnectionDetector(
    private val apiHostname: String,
    private val lanApiAddress: String,
    private val remoteApiAddress: String,
    private val localNetworkSource: LocalNetworkSource,
    private val healthProbe: HealthProbe,
) {
    suspend fun detect(): ConnectionStatus {
        val local = localNetworkSource.currentBaseNetwork()
        var tlsFailure = false
        if (local != null && local.addresses.any { sameSubnet(it, lanApiAddress) }) {
            when (
                val status =
                    probe(
                        ProbeTarget(apiHostname, lanApiAddress, local.networkId),
                        ConnectionRoute.LOCAL_DIRECT,
                    )
            ) {
                is ConnectionStatus.Connected -> return status
                ConnectionStatus.TlsFailure -> tlsFailure = true
                else -> Unit
            }
        }

        return when (
            val status =
                probe(
                    ProbeTarget(apiHostname, remoteApiAddress, null),
                    ConnectionRoute.REMOTE_SECURE,
                )
        ) {
            is ConnectionStatus.Connected -> status
            ConnectionStatus.TlsFailure -> ConnectionStatus.TlsFailure
            else -> if (tlsFailure) ConnectionStatus.TlsFailure else ConnectionStatus.Disconnected
        }
    }

    private suspend fun probe(
        target: ProbeTarget,
        route: ConnectionRoute,
    ): ConnectionStatus? =
        try {
            val health = healthProbe.check(target)
            if (health.protocolVersion != EXPECTED_PROTOCOL_VERSION) {
                null
            } else {
                ConnectionStatus.Connected(route, health.storage)
            }
        } catch (_: SSLException) {
            ConnectionStatus.TlsFailure
        } catch (_: Exception) {
            null
        }

    @Suppress("MagicNumber", "ReturnCount")
    internal fun sameSubnet(
        local: InterfaceAddress,
        targetAddress: String,
    ): Boolean {
        val target = InetAddress.getByName(targetAddress).address
        val source = local.address.address
        if (target.size != source.size || local.prefixLength !in 0..source.size * 8) return false
        val fullBytes = local.prefixLength / 8
        val remainingBits = local.prefixLength % 8
        for (index in 0 until fullBytes) {
            if (source[index] != target[index]) return false
        }
        if (remainingBits == 0) return true
        val mask = (0xff shl (8 - remainingBits)) and 0xff
        return (source[fullBytes].toInt() and mask) == (target[fullBytes].toInt() and mask)
    }

    private companion object {
        const val EXPECTED_PROTOCOL_VERSION = 1
    }
}
