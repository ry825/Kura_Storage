package com.kurastorage.core.network

import com.kurastorage.core.model.ConnectionRoute
import com.kurastorage.core.model.ConnectionStatus
import com.kurastorage.core.model.ServerHealth
import com.kurastorage.core.model.StorageAvailability
import kotlinx.coroutines.test.runTest
import org.junit.Assert.assertEquals
import org.junit.Test
import java.net.InetAddress
import javax.net.ssl.SSLHandshakeException

class ConnectionDetectorTest {
    @Test
    fun `local direct wins when local and remote are both reachable`() =
        runTest {
            val targets = mutableListOf<ProbeTarget>()
            val detector =
                detector(localAddress = "192.168.20.15", prefix = 24) { target ->
                    targets += target
                    available()
                }

            assertEquals(
                ConnectionStatus.Connected(ConnectionRoute.LOCAL_DIRECT, StorageAvailability.AVAILABLE),
                detector.detect(),
            )
            assertEquals(listOf("192.168.20.2"), targets.map { it.address })
            assertEquals("base-network", targets.single().bindNetworkId)
        }

    @Test
    fun `AP isolation does not become local direct and remote fallback is used`() =
        runTest {
            val targets = mutableListOf<ProbeTarget>()
            val detector =
                detector(localAddress = "192.168.20.15", prefix = 24) { target ->
                    targets += target
                    if (target.bindNetworkId != null) error("AP isolation") else available()
                }

            assertEquals(
                ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE),
                detector.detect(),
            )
            assertEquals(listOf("192.168.20.2", "10.44.0.2"), targets.map { it.address })
        }

    @Test
    fun `different subnet cannot become local direct even if LAN address is reachable elsewhere`() =
        runTest {
            val targets = mutableListOf<ProbeTarget>()
            val detector =
                detector(localAddress = "192.168.30.15", prefix = 24) { target ->
                    targets += target
                    available()
                }

            assertEquals(
                ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE),
                detector.detect(),
            )
            assertEquals(listOf("10.44.0.2"), targets.map { it.address })
        }

    @Test
    fun `TLS failure is distinct from disconnected`() =
        runTest {
            val detector =
                detector(localAddress = null) {
                    throw SSLHandshakeException("test certificate rejected")
                }

            assertEquals(ConnectionStatus.TlsFailure, detector.detect())
        }

    @Test
    fun `remote secure is used when local TLS fails but remote succeeds`() =
        runTest {
            val detector =
                detector(localAddress = "192.168.20.15", prefix = 24) { target ->
                    if (target.bindNetworkId != null) {
                        throw SSLHandshakeException("local certificate rejected")
                    }
                    available()
                }

            assertEquals(
                ConnectionStatus.Connected(ConnectionRoute.REMOTE_SECURE, StorageAvailability.AVAILABLE),
                detector.detect(),
            )
        }

    @Test
    fun `unreachable local and remote is disconnected`() =
        runTest {
            val detector =
                detector(localAddress = "192.168.20.15", prefix = 24) {
                    error("unreachable")
                }

            assertEquals(ConnectionStatus.Disconnected, detector.detect())
        }

    private fun detector(
        localAddress: String?,
        prefix: Int = 24,
        probe: suspend (ProbeTarget) -> ServerHealth,
    ): ConnectionDetector =
        ConnectionDetector(
            apiHostname = "api.kurastorage.example",
            lanApiAddress = "192.168.20.2",
            remoteApiAddress = "10.44.0.2",
            localNetworkSource =
                LocalNetworkSource {
                    localAddress?.let {
                        LocalNetworkSnapshot(
                            "base-network",
                            listOf(InterfaceAddress(InetAddress.getByName(it), prefix)),
                        )
                    }
                },
            healthProbe = HealthProbe(probe),
        )

    @Test
    fun `protocol 1 is rejected before file access`() =
        runTest {
            val detector = detector(localAddress = null) { ServerHealth(1, StorageAvailability.AVAILABLE) }

            assertEquals(ConnectionStatus.IncompatibleProtocol, detector.detect())
        }

    private fun available() = ServerHealth(2, StorageAvailability.AVAILABLE)
}
