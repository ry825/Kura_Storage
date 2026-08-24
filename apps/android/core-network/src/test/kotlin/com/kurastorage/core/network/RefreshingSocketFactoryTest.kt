package com.kurastorage.core.network

import org.junit.Assert.assertSame
import org.junit.Assert.assertThrows
import org.junit.Test
import java.net.InetAddress
import java.net.Socket
import java.net.SocketException
import javax.net.SocketFactory

class RefreshingSocketFactoryTest {
    @Test
    fun `each connection uses the current socket factory`() {
        val firstSocket = Socket()
        val secondSocket = Socket()
        val first = FakeSocketFactory(firstSocket)
        val second = FakeSocketFactory(secondSocket)
        var current: SocketFactory = first
        val factory = RefreshingSocketFactory { current }

        assertSame(firstSocket, factory.createSocket())

        current = second
        assertSame(secondSocket, factory.createSocket())
    }

    @Test
    fun `missing local network is reported as a recoverable socket failure`() {
        val factory = RefreshingSocketFactory { null }

        assertThrows(SocketException::class.java) { factory.createSocket() }
    }

    private class FakeSocketFactory(
        private val socket: Socket,
    ) : SocketFactory() {
        override fun createSocket(): Socket = socket

        override fun createSocket(
            host: String,
            port: Int,
        ): Socket = socket

        override fun createSocket(
            host: String,
            port: Int,
            localHost: InetAddress,
            localPort: Int,
        ): Socket = socket

        override fun createSocket(
            host: InetAddress,
            port: Int,
        ): Socket = socket

        override fun createSocket(
            address: InetAddress,
            port: Int,
            localAddress: InetAddress,
            localPort: Int,
        ): Socket = socket
    }
}
