package com.kurastorage.core.model

enum class ErrorCode {
    VALIDATION_FAILED,
    AUTHENTICATION_REQUIRED,
    DEVICE_REGISTRATION_REQUIRES_LOCAL_DIRECT,
    DEVICE_REVOKED,
    REFRESH_TOKEN_REUSED,
    STORAGE_UNAVAILABLE,
    STORAGE_CAPACITY_INSUFFICIENT,
    FILE_NOT_FOUND,
    FILE_NAME_CONFLICT,
    FILE_RESTORE_CONFLICT,
    IDEMPOTENCY_CONFLICT,
    UPLOAD_SIZE_MISMATCH,
    UPLOAD_CHECKSUM_MISMATCH,
    INTERNAL_ERROR,
    UNKNOWN,
}

enum class ErrorCategory {
    STORAGE,
    CONFLICT,
    AUTHORIZATION,
    AUTHENTICATION,
    VALIDATION,
    CONNECTION,
    UNKNOWN,
}

data class ApiError(
    val code: ErrorCode,
    val requestId: String?,
    val statusCode: Int?,
) {
    val category: ErrorCategory
        get() =
            when (code) {
                ErrorCode.STORAGE_UNAVAILABLE, ErrorCode.STORAGE_CAPACITY_INSUFFICIENT -> ErrorCategory.STORAGE
                ErrorCode.FILE_NAME_CONFLICT,
                ErrorCode.FILE_RESTORE_CONFLICT,
                ErrorCode.IDEMPOTENCY_CONFLICT,
                -> ErrorCategory.CONFLICT
                ErrorCode.FILE_NOT_FOUND -> ErrorCategory.AUTHORIZATION
                ErrorCode.AUTHENTICATION_REQUIRED,
                ErrorCode.DEVICE_REVOKED,
                ErrorCode.REFRESH_TOKEN_REUSED,
                -> ErrorCategory.AUTHENTICATION
                ErrorCode.VALIDATION_FAILED,
                ErrorCode.UPLOAD_SIZE_MISMATCH,
                ErrorCode.UPLOAD_CHECKSUM_MISMATCH,
                -> ErrorCategory.VALIDATION
                else -> ErrorCategory.UNKNOWN
            }

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
