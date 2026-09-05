package com.kurastorage.core.data.media

import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.formatUserFileSize
import com.kurastorage.core.model.media.ByteCount
import com.kurastorage.core.model.media.MediaKind
import com.kurastorage.core.model.media.MediaVariant
import com.kurastorage.core.model.media.OriginalMetadata

data class TransferConfirmationPrompt(
    val fileId: String,
    val fileVersion: Long,
    val kind: MediaKind,
    val variant: MediaVariant,
    val size: ByteCount?,
    val acceptsRanges: Boolean?,
    val formattedSize: String,
    val description: String,
) {
    fun approve(): TransferApproval = TransferApproval(fileId, fileVersion, variant, size)
}

data class TransferApproval(
    private val fileId: String,
    private val fileVersion: Long,
    private val variant: MediaVariant,
    private val size: ByteCount?,
) {
    fun matches(
        fileId: String,
        fileVersion: Long,
        variant: MediaVariant,
        size: ByteCount?,
    ): Boolean =
        this.fileId == fileId &&
            this.fileVersion == fileVersion &&
            this.variant == variant &&
            this.size == size
}

class TransferConfirmationPolicy(
    private val repository: MediaRepository,
) {
    suspend fun prepare(
        fileId: String,
        fileVersion: Long,
        kind: MediaKind,
        observedMetadata: OriginalMetadata? = null,
    ): TransferConfirmationPrompt {
        require(fileId.isNotBlank())
        require(fileVersion >= 0)
        val metadata =
            observedMetadata
                ?: try {
                    repository.inspectOriginal(fileId)
                } catch (error: KuraStorageException) {
                    when (error) {
                        is KuraStorageException.Network,
                        is KuraStorageException.InvalidServerResponse,
                        -> null
                        is KuraStorageException.Api ->
                            if (
                                error.error.statusCode == TOO_MANY_REQUESTS_STATUS ||
                                (error.error.statusCode ?: 0) >= SERVER_ERROR_STATUS
                            ) {
                                null
                            } else {
                                throw error
                            }
                        else -> throw error
                    }
                }
        val formattedSize = metadata?.size?.formatIec() ?: "Size unavailable"
        return TransferConfirmationPrompt(
            fileId = fileId,
            fileVersion = fileVersion,
            kind = kind,
            variant = MediaVariant.ORIGINAL,
            size = metadata?.size,
            acceptsRanges = metadata?.acceptsRanges,
            formattedSize = formattedSize,
            description = transferDescription(kind, formattedSize, metadata != null),
        )
    }
}

private fun transferDescription(
    kind: MediaKind,
    formattedSize: String,
    sizeKnown: Boolean,
): String =
    when {
        !sizeKnown -> "Data use could not be determined. Content starts only after you confirm."
        kind == MediaKind.VIDEO || kind == MediaKind.AUDIO ->
            "Up to $formattedSize may be transferred; range playback may use less. Actual data use varies."
        else -> "About $formattedSize may be transferred. Actual data use varies by file and format."
    }

fun ByteCount.formatIec(): String = formatUserFileSize(value)

private const val TOO_MANY_REQUESTS_STATUS = 429
private const val SERVER_ERROR_STATUS = 500
