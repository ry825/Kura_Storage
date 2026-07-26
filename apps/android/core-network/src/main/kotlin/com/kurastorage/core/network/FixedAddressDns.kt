package com.kurastorage.core.network

import okhttp3.Dns
import java.net.InetAddress
import java.net.UnknownHostException

class FixedAddressDns(
    private val apiHostname: String,
    address: String,
) : Dns {
    private val fixedAddress = InetAddress.getByName(address)

    override fun lookup(hostname: String): List<InetAddress> {
        if (!hostname.equals(apiHostname, ignoreCase = true)) {
            throw UnknownHostException("Only the configured API hostname may be resolved")
        }
        return listOf(fixedAddress)
    }
}
