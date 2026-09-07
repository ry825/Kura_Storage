package com.kurastorage.core.data.media

import com.kurastorage.core.data.AuthenticatedCallResult
import com.kurastorage.core.data.AuthenticatedRequestExecutor
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.media.MediaDerivativeStatus
import com.kurastorage.core.network.AdminMediaDerivativeApi
import com.kurastorage.core.network.MediaDerivativeStatusDto
import com.kurastorage.core.network.NetworkCallResult
import java.time.Instant

interface AdminMediaDerivativeRepository {
    suspend fun get(): MediaDerivativeStatus
}

class DefaultAdminMediaDerivativeRepository(
    private val api: AdminMediaDerivativeApi,
    private val executor: AuthenticatedRequestExecutor,
) : AdminMediaDerivativeRepository {
    override suspend fun get(): MediaDerivativeStatus =
        executor
            .execute { token ->
                when (val result = api.getMediaDerivatives(token)) {
                    is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                    NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
                }
            }.toModel()
}

internal fun MediaDerivativeStatusDto.toModel(): MediaDerivativeStatus {
    val values =
        listOf(
            photoCount,
            readyCount,
            pendingCount,
            runningCount,
            failedCount,
            blockedCount,
            missingCount,
            readyBytes,
            duplicateCount,
            orphanCount,
        )
    if (profileVersion < 1 ||
        values.any { it < 0 } ||
        readyCount + pendingCount + runningCount + failedCount + blockedCount + missingCount != photoCount
    ) {
        throw KuraStorageException.InvalidServerResponse()
    }
    return MediaDerivativeStatus(
        profileVersion,
        photoCount,
        readyCount,
        pendingCount,
        runningCount,
        failedCount,
        blockedCount,
        missingCount,
        readyBytes,
        duplicateCount,
        orphanCount,
        runCatching { Instant.parse(observedAt) }.getOrElse { throw KuraStorageException.InvalidServerResponse() },
    )
}
