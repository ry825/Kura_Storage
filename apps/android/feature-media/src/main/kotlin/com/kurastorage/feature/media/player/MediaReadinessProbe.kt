package com.kurastorage.feature.media.player

import com.kurastorage.core.data.media.MediaContentResult
import com.kurastorage.core.data.media.MediaRepository
import com.kurastorage.core.model.media.MediaJobSnapshot
import com.kurastorage.feature.media.MediaRequestTicket

sealed interface MediaReadiness {
    data object Ready : MediaReadiness

    data class Generating(
        val job: MediaJobSnapshot,
    ) : MediaReadiness
}

fun interface MediaReadinessProbe {
    suspend fun check(ticket: MediaRequestTicket): MediaReadiness
}

class RepositoryMediaReadinessProbe(
    private val repository: MediaRepository,
) : MediaReadinessProbe {
    override suspend fun check(ticket: MediaRequestTicket): MediaReadiness =
        when (
            val result =
                repository.openContent(
                    ticket.source.fileId,
                    ticket.source.variant,
                    FIRST_BYTE_RANGE,
                )
        ) {
            is MediaContentResult.Ready -> {
                result.content.close()
                MediaReadiness.Ready
            }
            is MediaContentResult.Generating -> MediaReadiness.Generating(result.job)
        }

    private companion object {
        const val FIRST_BYTE_RANGE = "bytes=0-0"
    }
}
