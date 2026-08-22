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
    FILE_MISSING_CANDIDATE,
    FILE_MISSING,
    FILE_STATE_CONFLICT,
    INDEX_CONFLICT,
    FILE_NAME_CONFLICT,
    FILE_MOVE_CYCLE,
    FILE_OPERATION_NOT_ALLOWED,
    FILE_RESTORE_CONFLICT,
    RECOVERY_REQUIRED,
    IDEMPOTENCY_CONFLICT,
    UPLOAD_SIZE_MISMATCH,
    UPLOAD_CHECKSUM_MISMATCH,
    UPLOAD_SESSION_NOT_FOUND,
    UPLOAD_OFFSET_MISMATCH,
    UPLOAD_INCOMPLETE,
    UPLOAD_SESSION_EXPIRED,
    UPLOAD_SESSION_CANCELLED,
    UPLOAD_SESSION_COMPLETED,
    CHUNK_SIZE_LIMIT_EXCEEDED,
    FILE_SIZE_LIMIT_EXCEEDED,
    CHUNK_CHECKSUM_MISMATCH,
    UPLOAD_LIMIT_REACHED,
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
    val retryAfterSeconds: Long? = null,
    val uploadOffset: Long? = null,
) {
    val category: ErrorCategory
        get() =
            when (code) {
                ErrorCode.STORAGE_UNAVAILABLE,
                ErrorCode.STORAGE_CAPACITY_INSUFFICIENT,
                ErrorCode.RECOVERY_REQUIRED,
                -> ErrorCategory.STORAGE
                ErrorCode.FILE_NAME_CONFLICT,
                ErrorCode.FILE_MISSING_CANDIDATE,
                ErrorCode.FILE_MISSING,
                ErrorCode.FILE_STATE_CONFLICT,
                ErrorCode.INDEX_CONFLICT,
                ErrorCode.FILE_MOVE_CYCLE,
                ErrorCode.FILE_OPERATION_NOT_ALLOWED,
                ErrorCode.FILE_RESTORE_CONFLICT,
                ErrorCode.IDEMPOTENCY_CONFLICT,
                ErrorCode.UPLOAD_OFFSET_MISMATCH,
                ErrorCode.UPLOAD_INCOMPLETE,
                ErrorCode.UPLOAD_SESSION_EXPIRED,
                ErrorCode.UPLOAD_SESSION_CANCELLED,
                ErrorCode.UPLOAD_SESSION_COMPLETED,
                -> ErrorCategory.CONFLICT
                ErrorCode.FILE_NOT_FOUND,
                ErrorCode.UPLOAD_SESSION_NOT_FOUND,
                -> ErrorCategory.AUTHORIZATION
                ErrorCode.AUTHENTICATION_REQUIRED,
                ErrorCode.DEVICE_REVOKED,
                ErrorCode.REFRESH_TOKEN_REUSED,
                -> ErrorCategory.AUTHENTICATION
                ErrorCode.VALIDATION_FAILED,
                ErrorCode.UPLOAD_SIZE_MISMATCH,
                ErrorCode.UPLOAD_CHECKSUM_MISMATCH,
                ErrorCode.CHUNK_SIZE_LIMIT_EXCEEDED,
                ErrorCode.FILE_SIZE_LIMIT_EXCEEDED,
                ErrorCode.CHUNK_CHECKSUM_MISMATCH,
                -> ErrorCategory.VALIDATION
                else -> ErrorCategory.UNKNOWN
            }

    val canRetry: Boolean
        get() = statusCode == null || statusCode >= SERVER_ERROR_STATUS || code == ErrorCode.UPLOAD_LIMIT_REACHED

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

    class UploadSourceUnavailable(
        cause: Throwable? = null,
    ) : KuraStorageException("Upload source is no longer available", cause)

    class UploadSourceChanged : KuraStorageException("Upload source content or size changed")

    class ServerUpgradeRequired : KuraStorageException("The server does not support resumable uploads")
}
