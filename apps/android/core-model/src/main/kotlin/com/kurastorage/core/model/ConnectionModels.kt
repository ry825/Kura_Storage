package com.kurastorage.core.model

enum class ConnectionRoute {
    LOCAL_DIRECT,
    REMOTE_SECURE,
}

enum class StorageAvailability {
    AVAILABLE,
    UNAVAILABLE,
}

sealed interface ConnectionStatus {
    data object Checking : ConnectionStatus

    data class Connected(
        val route: ConnectionRoute,
        val storage: StorageAvailability,
    ) : ConnectionStatus

    data object Disconnected : ConnectionStatus

    data object TlsFailure : ConnectionStatus

    data object IncompatibleProtocol : ConnectionStatus
}

data class ServerHealth(
    val protocolVersion: Int,
    val storage: StorageAvailability,
)
