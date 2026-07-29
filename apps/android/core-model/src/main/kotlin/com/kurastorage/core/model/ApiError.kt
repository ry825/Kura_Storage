package com.kurastorage.core.model

enum class ErrorCode {
    VALIDATION_FAILED,
    AUTHENTICATION_REQUIRED,
    DEVICE_REGISTRATION_REQUIRES_LOCAL_DIRECT,
    DEVICE_REVOKED,
    REFRESH_TOKEN_REUSED,
    STORAGE_UNAVAILABLE,
    INTERNAL_ERROR,
    UNKNOWN,
}

data class ApiError(
    val code: ErrorCode,
    val requestId: String?,
    val statusCode: Int?,
) {
    val canRetry: Boolean
        get() = statusCode == null || statusCode >= SERVER_ERROR_STATUS

    private companion object {
        const val SERVER_ERROR_STATUS = 500
    }
}

sealed class KuraStorageException(
    message: String,
    cause: Throwable? = null,
) : Exception(message, cause) {
    class Api(
        val error: ApiError,
    ) : KuraStorageException("API request failed: ${error.code}")

    class Network(
        cause: Throwable,
    ) : KuraStorageException("Network request failed", cause)

    class CredentialUnavailable(
        cause: Throwable? = null,
    ) : KuraStorageException("Credential is unavailable", cause)
}
