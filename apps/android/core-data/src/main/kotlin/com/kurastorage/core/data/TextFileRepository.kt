package com.kurastorage.core.data

import com.kurastorage.core.model.FileVersionChangeKind
import com.kurastorage.core.model.FileVersionItem
import com.kurastorage.core.model.FileVersionPage
import com.kurastorage.core.model.KuraStorageException
import com.kurastorage.core.model.TextDocument
import com.kurastorage.core.model.TextMutationResult
import com.kurastorage.core.network.FileVersionItemDto
import com.kurastorage.core.network.FileVersionPageDto
import com.kurastorage.core.network.NetworkCallResult
import com.kurastorage.core.network.RestoreTextVersionRequestDto
import com.kurastorage.core.network.SaveTextRequestDto
import com.kurastorage.core.network.TextDocumentDto
import com.kurastorage.core.network.TextFileApi
import com.kurastorage.core.network.TextMutationResultDto
import java.time.Instant

interface TextFileRepository {
    suspend fun current(fileId: String): TextDocument

    suspend fun save(
        fileId: String,
        content: String,
        expectedVersion: Long,
        operationId: String,
    ): TextMutationResult

    suspend fun versions(
        fileId: String,
        page: Int = 1,
        pageSize: Int = DEFAULT_PAGE_SIZE,
    ): FileVersionPage

    suspend fun version(
        fileId: String,
        version: Long,
    ): TextDocument

    suspend fun restore(
        fileId: String,
        version: Long,
        expectedVersion: Long,
        operationId: String,
    ): TextMutationResult

    companion object {
        const val DEFAULT_PAGE_SIZE = 50
    }
}

class DefaultTextFileRepository(
    private val api: TextFileApi,
    private val executor: AuthenticatedRequestExecutor,
) : TextFileRepository {
    override suspend fun current(fileId: String) = authenticated { api.getText(it, fileId) }.toModel()

    override suspend fun save(
        fileId: String,
        content: String,
        expectedVersion: Long,
        operationId: String,
    ) = authenticated { api.saveText(it, fileId, SaveTextRequestDto(content, expectedVersion, operationId)) }.toModel()

    override suspend fun versions(
        fileId: String,
        page: Int,
        pageSize: Int,
    ) = authenticated { api.listVersions(it, fileId, page, pageSize) }.toModel()

    override suspend fun version(
        fileId: String,
        version: Long,
    ) = authenticated { api.getVersionText(it, fileId, version) }.toModel()

    override suspend fun restore(
        fileId: String,
        version: Long,
        expectedVersion: Long,
        operationId: String,
    ) = authenticated {
        api.restoreVersion(it, fileId, version, RestoreTextVersionRequestDto(expectedVersion, operationId))
    }.toModel()

    private suspend fun <T> authenticated(call: suspend (String) -> NetworkCallResult<T>): T =
        executor.execute { token ->
            when (val result = call(token)) {
                is NetworkCallResult.Success -> AuthenticatedCallResult.Success(result.value)
                NetworkCallResult.Unauthorized -> AuthenticatedCallResult.Unauthorized
            }
        }
}

internal fun TextDocumentDto.toModel() = TextDocument(content, encoding, fileVersion, size, sha256)

internal fun TextMutationResultDto.toModel() =
    FileVersionChangeKind.fromWire(changeKind).let { kind ->
        if (kind !in setOf(FileVersionChangeKind.TEXT_EDIT, FileVersionChangeKind.RESTORE)) {
            throw KuraStorageException.InvalidServerResponse()
        }
        TextMutationResult(fileVersion, size, sha256, kind, Instant.parse(createdAt))
    }

internal fun FileVersionItemDto.toModel() =
    FileVersionItem(
        version,
        size,
        sha256,
        FileVersionChangeKind.fromWire(changeKind),
        actorDisplayName,
        Instant.parse(createdAt),
    )

@Suppress("MaxLineLength")
internal fun FileVersionPageDto.toModel() = FileVersionPage(items.map(FileVersionItemDto::toModel), page, pageSize, totalCount)
